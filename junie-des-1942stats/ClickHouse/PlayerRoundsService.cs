using System.Net.Http;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.PlayerTracking;
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
    /// Syncs completed PlayerSessions to ClickHouse player_rounds table
    /// </summary>
    public async Task<SyncResult> SyncCompletedSessionsAsync(DateTime? syncFromDate = null, int pageSize = 1000, int pageNumber = 0, bool excludeRecentData = false)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Default to syncing from 365 days ago if no date specified
            var fromDate = syncFromDate ?? DateTime.UtcNow.AddDays(-365);
            
            // Determine if this is incremental sync (pageNumber = 0 and no explicit fromDate) or manual paging
            var isIncrementalSync = pageNumber == 0 && syncFromDate == null;
            
            DateTime actualFromDate;
            if (isIncrementalSync)
            {
                // For incremental sync, use last synced timestamp from ClickHouse
                var lastSyncedTime = await GetLastSyncedTimestampAsync();
                actualFromDate = lastSyncedTime?.AddMinutes(-5) ?? fromDate; // 5 minute overlap for safety
                _logger.LogInformation("Starting incremental sync of completed player sessions from {FromDate}", actualFromDate);
            }
            else
            {
                // For manual paging, use the specified date range
                actualFromDate = fromDate;
                _logger.LogInformation("Starting manual sync of completed player sessions from {FromDate}, page {PageNumber}, pageSize {PageSize}", 
                    fromDate, pageNumber, pageSize);
            }

            // Use scoped DbContext for database access
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

            // Build the base query
            IQueryable<PlayerSession> query = dbContext.PlayerSessions
                .Where(ps => !ps.IsActive && ps.LastSeenTime >= actualFromDate);

            // For manual sync, exclude recent data to avoid conflicts with background service
            if (excludeRecentData)
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-2); // Exclude data newer than 2 hours
                query = query.Where(ps => ps.LastSeenTime <= cutoffTime);
                _logger.LogInformation("Manual sync excluding data newer than {CutoffTime} to avoid background service conflicts", cutoffTime);
            }

            query = query.OrderBy(ps => ps.SessionId); // Consistent ordering

            List<PlayerSession> completedSessions;
            bool hasMorePages = false;

            if (isIncrementalSync)
            {
                // For incremental sync, get all new records (no artificial paging)
                // But limit to reasonable batch size to avoid memory issues
                const int incrementalBatchSize = 5000;
                completedSessions = await query
                    .Take(incrementalBatchSize)
                    .Include(ps => ps.Observations.OrderByDescending(o => o.Timestamp).Take(1))
                    .ToListAsync();
                
                // Check if there are more records beyond this batch
                var totalCount = await dbContext.PlayerSessions
                    .Where(ps => !ps.IsActive && ps.LastSeenTime >= actualFromDate)
                    .CountAsync();
                hasMorePages = totalCount > incrementalBatchSize;
            }
            else
            {
                // For manual paging, use explicit pagination
                var totalCount = await query.CountAsync();
                completedSessions = await query
                    .Skip(pageNumber * pageSize)
                    .Take(pageSize)
                    .Include(ps => ps.Observations.OrderByDescending(o => o.Timestamp).Take(1))
                    .ToListAsync();
                hasMorePages = (pageNumber + 1) * pageSize < totalCount;
            }

            if (!completedSessions.Any())
            {
                _logger.LogInformation("No sessions found" + (isIncrementalSync ? " for incremental sync" : $" for page {pageNumber}"));
                return new SyncResult
                {
                    ProcessedCount = 0,
                    HasMorePages = false,
                    PageNumber = pageNumber,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var playerRounds = completedSessions.Select(ConvertToPlayerRound).ToList();
            
            await InsertPlayerRoundsAsync(playerRounds);
            
            var duration = DateTime.UtcNow - startTime;
            if (isIncrementalSync)
            {
                _logger.LogInformation("Successfully synced {Count} completed sessions to ClickHouse (incremental) in {Duration}ms", 
                    playerRounds.Count, duration.TotalMilliseconds);
            }
            else
            {
                _logger.LogInformation("Successfully synced {Count} completed sessions to ClickHouse (page {PageNumber}, hasMorePages: {HasMorePages}) in {Duration}ms", 
                    playerRounds.Count, pageNumber, hasMorePages, duration.TotalMilliseconds);
            }

            return new SyncResult
            {
                ProcessedCount = playerRounds.Count,
                HasMorePages = hasMorePages,
                PageNumber = pageNumber,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Failed to sync completed sessions" + (pageNumber > 0 ? $" on page {pageNumber}" : ""));
            return new SyncResult
            {
                ProcessedCount = 0,
                HasMorePages = false,
                PageNumber = pageNumber,
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
        // Create a deterministic round ID based on player, server, map, and start time
        var input = $"{session.PlayerName}_{session.ServerGuid}_{session.MapName}_{session.StartTime:yyyyMMddHHmmss}";
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
    round(SUM(final_kills) / SUM(final_deaths), 3) as kd_ratio
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
}

public class SyncResult
{
    public int ProcessedCount { get; set; }
    public bool HasMorePages { get; set; }
    public int PageNumber { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success => string.IsNullOrEmpty(ErrorMessage);
} 