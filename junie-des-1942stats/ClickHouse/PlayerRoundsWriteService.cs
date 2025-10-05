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

    private static int GetEnvInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(value, out var parsed) && parsed >= 0)
        {
            return parsed;
        }
        return defaultValue;
    }

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
    round_id String,
    player_name String,
    server_guid String,
    map_name String,
    round_start_time DateTime,
    round_end_time DateTime,
    final_score Int32,
    final_kills UInt32,
    final_deaths UInt32,
    play_time_minutes Float64,
    team_label String,
    game_id String,
    is_bot UInt8,
    created_at DateTime DEFAULT now(),
    game String,
    average_ping Nullable(Float64)
) ENGINE = ReplacingMergeTree()
ORDER BY round_id
PARTITION BY toYYYYMM(round_start_time)
SETTINGS index_granularity = 8192";

        await ExecuteCommandAsync(createTableQuery);

        // Add columns if they don't exist (for existing tables)
        await ExecuteCommandAsync("ALTER TABLE player_rounds ADD COLUMN IF NOT EXISTS is_bot UInt8 DEFAULT 0");
        await ExecuteCommandAsync("ALTER TABLE player_rounds ADD COLUMN IF NOT EXISTS game String DEFAULT 'unknown'");
        await ExecuteCommandAsync("ALTER TABLE player_rounds ADD COLUMN IF NOT EXISTS average_ping Nullable(Float64)");

        // Create indexes for common query patterns
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

    /// <summary>
    /// Syncs completed PlayerSessions to ClickHouse player_rounds table using idempotent sync
    /// </summary>
    public async Task<SyncResult> SyncCompletedSessionsAsync(int batchSize = 100_000)
    {
        var startTime = DateTime.UtcNow;
        var totalProcessedCount = 0;

        try
        {
            // Read runtime overrides
            var envBatchSize = GetEnvInt("PLAYER_ROUNDS_BATCH_SIZE", batchSize);
            var delayMs = GetEnvInt("PLAYER_ROUNDS_DELAY_MS", 100);
            var effectiveBatchSize = Math.Max(1, envBatchSize);

            // Use last synced timestamp from ClickHouse for incremental sync (read operation)
            var lastSyncedTime = await GetLastSyncedTimestampAsync();
            var fromDate = lastSyncedTime ?? DateTime.MinValue;
            var mode = lastSyncedTime.HasValue ? "Incremental" : "InitialLoad";

            _logger.LogInformation(
                "Starting sync of completed sessions. Mode={Mode}, BatchSize={BatchSize}, DelayMs={DelayMs}, FromDate={FromDate:o}, LastSynced={LastSynced:o}",
                mode, effectiveBatchSize, delayMs, fromDate, lastSyncedTime);

            // Use scoped DbContext for database access
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

            // Get total count for progress reporting
            var totalQuery = lastSyncedTime.HasValue
                ? dbContext.PlayerSessions.AsNoTracking().Where(ps => !ps.IsActive && ps.LastSeenTime > fromDate.AddHours(-1))
                : dbContext.PlayerSessions.AsNoTracking().Where(ps => !ps.IsActive && ps.LastSeenTime >= fromDate);

            var totalCount = await totalQuery.CountAsync();
            if (totalCount == 0)
            {
                _logger.LogInformation("No completed sessions found to sync");
                return new SyncResult
                {
                    ProcessedCount = 0,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            _logger.LogInformation("Found {TotalCount} sessions to sync", totalCount);

            // Process all records in batches
            var processedSoFar = 0;
            var batchNumber = 0;

            while (processedSoFar < totalCount)
            {
                batchNumber++;
                var batchStartTime = DateTime.UtcNow;

                // Get completed sessions since last sync, ordered consistently
                var baseQuery = lastSyncedTime.HasValue
                    ? dbContext.PlayerSessions.AsNoTracking().Where(ps => !ps.IsActive && ps.LastSeenTime > fromDate.AddHours(-1))
                    : dbContext.PlayerSessions.AsNoTracking().Where(ps => !ps.IsActive && ps.LastSeenTime >= fromDate);

                // Project minimal data needed for export
                var completedBatch = await baseQuery
                    .OrderBy(ps => ps.SessionId)
                    .Skip(processedSoFar)
                    .Take(effectiveBatchSize)
                    .Select(ps => new
                    {
                        ps.SessionId,
                        ps.PlayerName,
                        ps.ServerGuid,
                        ps.MapName,
                        ps.StartTime,
                        ps.LastSeenTime,
                        ps.TotalScore,
                        ps.TotalKills,
                        ps.TotalDeaths,
                        ps.GameType,
                        ps.RoundId,
                        ps.AveragePing,
                        AiBot = (bool?)ps.Player.AiBot,
                        Game = ps.Server.Game,
                        TeamLabel = ps.Observations
                            .OrderByDescending(o => o.Timestamp)
                            .Select(o => o.TeamLabel)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                if (!completedBatch.Any())
                {
                    break; // No more records to process
                }

                var playerRounds = completedBatch.Select(r => new PlayerRound
                {
                    PlayerName = r.PlayerName,
                    ServerGuid = r.ServerGuid,
                    MapName = r.MapName,
                    RoundStartTime = r.StartTime,
                    RoundEndTime = r.LastSeenTime,
                    FinalScore = r.TotalScore,
                    FinalKills = (uint)Math.Max(0, r.TotalKills),
                    FinalDeaths = (uint)Math.Max(0, r.TotalDeaths),
                    PlayTimeMinutes = Math.Max(0, (r.LastSeenTime - r.StartTime).TotalMinutes),
                    RoundId = r.RoundId ?? GenerateRoundId(r.PlayerName, r.ServerGuid, r.MapName, r.StartTime, r.SessionId),
                    TeamLabel = r.TeamLabel ?? string.Empty,
                    GameId = r.GameType,
                    Game = r.Game ?? "unknown",
                    IsBot = r.AiBot ?? false,
                    AveragePing = r.AveragePing,
                    CreatedAt = DateTime.UtcNow
                }).ToList();
                await InsertPlayerRoundsAsync(playerRounds);

                processedSoFar += playerRounds.Count;
                totalProcessedCount += playerRounds.Count;

                var batchDuration = DateTime.UtcNow - batchStartTime;
                _logger.LogInformation("Batch {BatchNumber}: Synced {BatchCount} sessions ({ProcessedSoFar}/{TotalCount}) in {Duration}ms",
                    batchNumber, playerRounds.Count, processedSoFar, totalCount, batchDuration.TotalMilliseconds);

                // Small delay to prevent overwhelming the database
                if (processedSoFar < totalCount)
                {
                    await Task.Delay(delayMs);
                }
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Successfully synced all {Count} completed sessions to ClickHouse in {Duration}ms across {BatchCount} batches",
                totalProcessedCount, duration.TotalMilliseconds, batchNumber);

            return new SyncResult
            {
                ProcessedCount = totalProcessedCount,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Failed to sync completed sessions after processing {ProcessedCount} records", totalProcessedCount);
            return new SyncResult
            {
                ProcessedCount = totalProcessedCount,
                Duration = duration,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GenerateRoundId(string playerName, string serverGuid, string mapName, DateTime startTime, long sessionId)
    {
        var input = $"{playerName}_{serverGuid}_{mapName}_{startTime:yyyyMMddHHmmss}_{sessionId}";
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

            // Write CSV records without header, round_id first to match new schema
            csvWriter.WriteRecords(rounds.Select(r => new
            {
                RoundId = r.RoundId,
                PlayerName = r.PlayerName,
                ServerGuid = r.ServerGuid,
                MapName = r.MapName,
                RoundStartTime = r.RoundStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                RoundEndTime = r.RoundEndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                FinalScore = r.FinalScore,
                FinalKills = r.FinalKills,
                FinalDeaths = r.FinalDeaths,
                PlayTimeMinutes = r.PlayTimeMinutes.ToString("F2", CultureInfo.InvariantCulture),
                TeamLabel = r.TeamLabel,
                GameId = r.GameId,
                IsBot = r.IsBot ? 1 : 0,
                CreatedAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                Game = r.Game,
                AveragePing = r.AveragePing.HasValue ? r.AveragePing.Value.ToString("F2", CultureInfo.InvariantCulture) : ""
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_rounds (round_id, player_name, server_guid, map_name, round_start_time, round_end_time, final_score, final_kills, final_deaths, play_time_minutes, team_label, game_id, is_bot, created_at, game, average_ping) FORMAT CSV";
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