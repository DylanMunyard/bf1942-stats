using System.Net.Http;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace junie_des_1942stats.ClickHouse;

public class PlayerRoundsWriteService : BaseClickHouseService, IClickHouseWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerRoundsWriteService> _logger;
    private readonly IClickHouseReader? _reader;

    public PlayerRoundsWriteService(
        HttpClient httpClient,
        string clickHouseUrl,
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerRoundsWriteService> logger,
        IClickHouseReader? reader = null)
        : base(httpClient, clickHouseUrl)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _reader = reader;
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

        await ExecuteCommandAsync(createTableQuery);

        // Create indexes
        var indexQueries = new[]
        {
            "ALTER TABLE player_rounds ADD INDEX IF NOT EXISTS idx_player_time (player_name, round_start_time) TYPE minmax GRANULARITY 1",
            "ALTER TABLE player_rounds ADD INDEX IF NOT EXISTS idx_time_player (round_start_time, player_name) TYPE bloom_filter GRANULARITY 1"
        };

        foreach (var indexQuery in indexQueries)
        {
            try
            {
                await ExecuteCommandAsync(indexQuery);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create index, might already exist: {Query}", indexQuery);
            }
        }
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }

    /// <summary>
    /// Syncs completed PlayerSessions to ClickHouse player_rounds table using incremental sync
    /// </summary>
    public async Task<SyncResult> SyncCompletedSessionsAsync(int batchSize = 5000)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Use last synced timestamp from ClickHouse for incremental sync (read operation)
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

            // Use reader if available, otherwise use write connection for this read query
            string result;
            if (_reader != null)
            {
                result = await _reader.ExecuteQueryAsync(query);
            }
            else
            {
                result = await ExecuteQueryInternalAsync(query);
            }

            if (DateTime.TryParse(result.Trim(), out var lastTime))
            {
                return lastTime;
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

            await ExecuteCommandAsync(fullRequest);
            _logger.LogInformation("Successfully inserted {Count} player rounds to ClickHouse", rounds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert player rounds to ClickHouse");
            throw;
        }
    }
}

public class SyncResult
{
    public int ProcessedCount { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success => string.IsNullOrEmpty(ErrorMessage);
}