using System.Net.Http;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace junie_des_1942stats.ClickHouse;

public class PlayerRoundsService
{
    private readonly HttpClient _httpClient;
    private readonly string _clickHouseUrl;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerRoundsService> _logger;

    public PlayerRoundsService(HttpClient httpClient, string clickHouseUrl, IServiceScopeFactory scopeFactory, ILogger<PlayerRoundsService> logger)
    {
        _httpClient = httpClient;
        _clickHouseUrl = clickHouseUrl.TrimEnd('/');
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the player_rounds table is created in ClickHouse
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        try
        {
            await CreatePlayerRoundsTableAsync();
            _logger.LogInformation("ClickHouse player_rounds schema verified/created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure ClickHouse player_rounds schema");
            throw;
        }
    }

    private async Task CreatePlayerRoundsTableAsync()
    {
        var createTableQuery = @"
CREATE TABLE IF NOT EXISTS player_rounds (
    player_name String,
    server_guid String,
    map_name String,
    round_start_time DateTime,
    round_end_time DateTime,
    final_score Int32,
    final_kills UInt32,
    final_deaths UInt32,
    play_time_minutes Float64,
    round_id String,
    team_label String,
    game_id String,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (player_name, server_guid, round_start_time)
PARTITION BY toYYYYMM(round_start_time)
SETTINGS index_granularity = 8192";

        await ExecuteQueryAsync(createTableQuery);

        // Create indexes for fast similarity searches as mentioned in README
        var indexQueries = new[]
        {
            "ALTER TABLE player_rounds ADD INDEX IF NOT EXISTS idx_player_time (player_name, round_start_time) TYPE minmax GRANULARITY 1",
            "ALTER TABLE player_rounds ADD INDEX IF NOT EXISTS idx_time_player (round_start_time, player_name) TYPE bloom_filter GRANULARITY 1"
        };

        foreach (var indexQuery in indexQueries)
        {
            try
            {
                await ExecuteQueryAsync(indexQuery);
            }
            catch (Exception ex)
            {
                // Indexes might already exist or fail for other reasons, just log and continue
                _logger.LogWarning(ex, "Failed to create index, might already exist: {Query}", indexQuery);
            }
        }
    }

    private async Task ExecuteQueryAsync(string query)
    {
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"ClickHouse query failed: {response.StatusCode} - {errorContent}");
        }
    }

    /// <summary>
    /// Syncs completed PlayerSessions to ClickHouse player_rounds table using incremental sync
    /// </summary>
    public async Task<SyncResult> SyncCompletedSessionsAsync(int batchSize = 5000)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Use last synced timestamp from ClickHouse for incremental sync
            var lastSyncedTime = await GetLastSyncedTimestampAsync();
            var fromDate = lastSyncedTime ?? DateTime.UtcNow.AddDays(-365);
            
            _logger.LogInformation("Starting incremental sync of completed player sessions from {FromDate}", fromDate);

            // Use scoped DbContext for database access
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

            // Get completed sessions since last sync, ordered consistently
            // Use > instead of >= to avoid re-syncing the exact last record and prevent duplicates
            var query = lastSyncedTime.HasValue 
                ? dbContext.PlayerSessions.Where(ps => !ps.IsActive && ps.LastSeenTime > fromDate)
                : dbContext.PlayerSessions.Where(ps => !ps.IsActive && ps.LastSeenTime >= fromDate);
                
            var completedSessions = await query
                .OrderBy(ps => ps.SessionId)
                .Take(batchSize)
                .Include(ps => ps.Observations.OrderByDescending(o => o.Timestamp).Take(1))
                .ToListAsync();

            if (!completedSessions.Any())
            {
                _logger.LogInformation("No new sessions found for incremental sync");
                return new SyncResult
                {
                    ProcessedCount = 0,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var playerRounds = completedSessions.Select(ConvertToPlayerRound).ToList();
            await InsertPlayerRoundsAsync(playerRounds);
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Successfully synced {Count} completed sessions to ClickHouse in {Duration}ms", 
                playerRounds.Count, duration.TotalMilliseconds);

            return new SyncResult
            {
                ProcessedCount = playerRounds.Count,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Failed to sync completed sessions");
            return new SyncResult
            {
                ProcessedCount = 0,
                Duration = duration,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<DateTime?> GetLastSyncedTimestampAsync()
    {
        try
        {
            var query = "SELECT MAX(round_end_time) FROM player_rounds";
            var content = new StringContent(query, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                if (DateTime.TryParse(result.Trim(), out var lastTime))
                {
                    return lastTime;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last synced timestamp, will sync from beginning");
        }
        
        return null;
    }

    private PlayerRound ConvertToPlayerRound(PlayerSession session)
    {
        // Calculate play time in minutes
        var playTimeMinutes = (session.LastSeenTime - session.StartTime).TotalMinutes;
        
        // Generate a unique round ID
        var roundId = GenerateRoundId(session);
        
        // Get team label from the last observation if available
        var teamLabel = session.Observations?.LastOrDefault()?.TeamLabel ?? "";
        
        return new PlayerRound
        {
            PlayerName = session.PlayerName,
            ServerGuid = session.ServerGuid,
            MapName = session.MapName,
            RoundStartTime = session.StartTime,
            RoundEndTime = session.LastSeenTime,
            FinalScore = session.TotalScore,
            FinalKills = (uint)Math.Max(0, session.TotalKills),
            FinalDeaths = (uint)Math.Max(0, session.TotalDeaths),
            PlayTimeMinutes = Math.Max(0, playTimeMinutes),
            RoundId = roundId,
            TeamLabel = teamLabel,
            GameId = session.GameType,
            CreatedAt = DateTime.UtcNow
        };
    }

    private string GenerateRoundId(PlayerSession session)
    {
        // Create a deterministic round ID based on player, server, map, start time, and session ID
        // Including SessionId ensures uniqueness even for rapid reconnections
        var input = $"{session.PlayerName}_{session.ServerGuid}_{session.MapName}_{session.StartTime:yyyyMMddHHmmss}_{session.SessionId}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];
    }

    private async Task InsertPlayerRoundsAsync(List<PlayerRound> rounds)
    {
        if (!rounds.Any())
            return;

        try
        {
            // Use CsvHelper to generate properly formatted CSV data
            using var stringWriter = new StringWriter();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            // Write CSV records without header
            csvWriter.WriteRecords(rounds.Select(r => new
            {
                PlayerName = r.PlayerName,
                ServerGuid = r.ServerGuid,
                MapName = r.MapName,
                RoundStartTime = r.RoundStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                RoundEndTime = r.RoundEndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                FinalScore = r.FinalScore,
                FinalKills = r.FinalKills,
                FinalDeaths = r.FinalDeaths,
                PlayTimeMinutes = r.PlayTimeMinutes.ToString("F2", CultureInfo.InvariantCulture),
                RoundId = r.RoundId,
                TeamLabel = r.TeamLabel,
                GameId = r.GameId,
                CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_rounds (player_name, server_guid, map_name, round_start_time, round_end_time, final_score, final_kills, final_deaths, play_time_minutes, round_id, team_label, game_id, created_at) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            var content = new StringContent(fullRequest, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("ClickHouse insert error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            }
            else
            {
                _logger.LogInformation("Successfully inserted {Count} player rounds to ClickHouse", rounds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert player rounds to ClickHouse");
            throw;
        }
    }

    /// <summary>
    /// Query aggregated player statistics from the player_rounds table
    /// </summary>
    public async Task<string> GetPlayerStatsAsync(string? playerName = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var conditions = new List<string>();
        
        if (!string.IsNullOrEmpty(playerName))
            conditions.Add($"player_name = '{playerName.Replace("'", "''")}'");
            
        if (fromDate.HasValue)
            conditions.Add($"round_start_time >= '{fromDate.Value:yyyy-MM-dd HH:mm:ss}'");
            
        if (toDate.HasValue)
            conditions.Add($"round_start_time <= '{toDate.Value:yyyy-MM-dd HH:mm:ss}'");

        var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";

        var query = $@"
SELECT 
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths, 
    SUM(play_time_minutes) as total_play_time_minutes,
    AVG(final_score) as avg_score_per_round,
    CASE WHEN SUM(final_deaths) > 0 THEN round(SUM(final_kills) / SUM(final_deaths), 3) ELSE toFloat64(SUM(final_kills)) END as kd_ratio
FROM player_rounds 
{whereClause}
GROUP BY player_name
HAVING total_kills > 10 
ORDER BY total_play_time_minutes DESC
FORMAT TabSeparatedWithNames";

        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"ClickHouse query failed: {response.StatusCode} - {errorContent}");
        }

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Get most active players by time played from ClickHouse
    /// </summary>
    public async Task<List<PlayerActivity>> GetMostActivePlayersAsync(string serverGuid, DateTime startPeriod, DateTime endPeriod, int limit = 10)
    {
        var query = $@"
SELECT 
    player_name,
    CAST(SUM(play_time_minutes) AS INTEGER) as minutes_played,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
  AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
  AND round_end_time <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY player_name
ORDER BY minutes_played DESC
LIMIT {limit}
FORMAT TabSeparated";

        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ClickHouse query failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return new List<PlayerActivity>();
        }

        var result = await response.Content.ReadAsStringAsync();
        var players = new List<PlayerActivity>();
        
        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 4)
            {
                players.Add(new PlayerActivity
                {
                    PlayerName = parts[0],
                    MinutesPlayed = int.Parse(parts[1]),
                    TotalKills = int.Parse(parts[2]),
                    TotalDeaths = int.Parse(parts[3])
                });
            }
        }

        return players;
    }

    /// <summary>
    /// Get top scores from ClickHouse
    /// </summary>
    public async Task<List<TopScore>> GetTopScoresAsync(string serverGuid, DateTime startPeriod, DateTime endPeriod, int limit = 10)
    {
        var query = $@"
SELECT 
    player_name,
    final_score,
    final_kills,
    final_deaths,
    map_name,
    round_end_time,
    round_id
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
  AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
  AND round_end_time <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
ORDER BY final_score DESC
LIMIT {limit}
FORMAT TabSeparated";

        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ClickHouse query failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return new List<TopScore>();
        }

        var result = await response.Content.ReadAsStringAsync();
        var topScores = new List<TopScore>();
        
        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 7)
            {
                topScores.Add(new TopScore
                {
                    PlayerName = parts[0],
                    Score = int.Parse(parts[1]),
                    Kills = int.Parse(parts[2]),
                    Deaths = int.Parse(parts[3]),
                    MapName = parts[4],
                    Timestamp = DateTime.Parse(parts[5]),
                    SessionId = parts[6].GetHashCode() // Use round_id hash as session ID substitute
                });
            }
        }

        return topScores;
    }

    /// <summary>
    /// Get last rounds from ClickHouse
    /// </summary>
    public async Task<List<RoundInfo>> GetLastRoundsAsync(string serverGuid, DateTime recentRoundsStart, int limit = 5)
    {
        var query = $@"
SELECT 
    map_name,
    MAX(round_start_time) as start_time,
    MAX(round_end_time) as end_time
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
  AND round_start_time >= '{recentRoundsStart:yyyy-MM-dd HH:mm:ss}'
GROUP BY map_name
ORDER BY start_time DESC
LIMIT {limit}
FORMAT TabSeparated";

        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ClickHouse query failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return new List<RoundInfo>();
        }

        var result = await response.Content.ReadAsStringAsync();
        var lastRounds = new List<RoundInfo>();
        
        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                lastRounds.Add(new RoundInfo
                {
                    MapName = parts[0],
                    StartTime = DateTime.Parse(parts[1]),
                    EndTime = DateTime.Parse(parts[2]),
                    IsActive = false // ClickHouse only contains completed rounds
                });
            }
        }

        return lastRounds;
    }
}

public class SyncResult
{
    public int ProcessedCount { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success => string.IsNullOrEmpty(ErrorMessage);
} 