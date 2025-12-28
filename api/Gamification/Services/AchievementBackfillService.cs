using api.Gamification.Models;
using api.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using ClickHouse.Client.ADO;

namespace api.Gamification.Services;

/// <summary>
/// Service for backfilling achievements from ClickHouse to SQLite.
/// Migrates existing achievement data during the ClickHouse migration.
/// </summary>
public class AchievementBackfillService(
    SqliteGamificationService sqliteService,
    ILogger<AchievementBackfillService> logger) : IDisposable
{
    private readonly ClickHouseConnection _clickHouseConnection = InitializeClickHouseConnection(logger);
    private bool _disposed;


    /// <summary>
    /// Deprecated achievement IDs from PerformanceBadgeCalculator - exclude from backfill
    /// </summary>
    private static readonly string[] DeprecatedAchievementIds =
    [
        "sharpshooter_bronze", "sharpshooter_silver", "sharpshooter_gold", "sharpshooter_legend",
        "elite_warrior_bronze", "elite_warrior_silver", "elite_warrior_gold", "elite_warrior_legend",
        "consistent_killer"
    ];

    private static ClickHouseConnection InitializeClickHouseConnection(ILogger logger)
    {
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        try
        {
            var uri = new Uri(clickHouseUrl);
            var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
            var connection = new ClickHouseConnection(connectionString);
            logger.LogInformation("ClickHouse connection initialized for backfill: {Url}", clickHouseUrl);
            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize ClickHouse connection for backfill");
            throw;
        }
    }

    /// <summary>
    /// Backfill all achievements from ClickHouse to SQLite
    /// </summary>
    public async Task<BackfillResult> BackfillAllAchievementsAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var activity = ActivitySources.Backfill.StartActivity("BackfillAllAchievements");
            activity?.SetTag("operation", "full_backfill");

            logger.LogInformation("Starting full achievement backfill from ClickHouse to SQLite");

            // Get total count first for progress tracking
            var totalCount = await GetClickHouseAchievementCountAsync();
            activity?.SetTag("total_count", totalCount);

            logger.LogInformation("Found {TotalCount} achievements to backfill", totalCount);

            if (totalCount == 0)
            {
                logger.LogInformation("No achievements to backfill");
                return new BackfillResult
                {
                    Success = true,
                    TotalCount = 0,
                    MigratedCount = 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ExecutedAtUtc = DateTime.UtcNow
                };
            }

            // Backfill in batches to avoid memory issues
            const int batchSize = 50_000;
            var totalMigrated = 0;
            var batchNumber = 0;

            while (true)
            {
                batchNumber++;
                var achievements = await GetClickHouseAchievementsBatchAsync(batchNumber - 1, batchSize);

                if (!achievements.Any())
                {
                    logger.LogInformation("No more achievements to backfill after batch {BatchNumber}", batchNumber);
                    break;
                }

                await sqliteService.InsertAchievementsBatchAsync(achievements);
                totalMigrated += achievements.Count;

                activity?.SetTag("batches_processed", batchNumber);
                activity?.SetTag("migrated_so_far", totalMigrated);

                logger.LogInformation("Processed batch {BatchNumber}: {BatchCount} achievements (total migrated: {TotalMigrated}/{TotalCount})",
                    batchNumber, achievements.Count, totalMigrated, totalCount);

                // Safety check - don't process more than expected
                if (totalMigrated > totalCount + batchSize)
                {
                    logger.LogWarning("Migrated count ({Migrated}) exceeds expected total ({Total}) - possible data inconsistency",
                        totalMigrated, totalCount);
                    break;
                }
            }

            stopwatch.Stop();

            var result = new BackfillResult
            {
                Success = true,
                TotalCount = totalCount,
                MigratedCount = totalMigrated,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ExecutedAtUtc = DateTime.UtcNow
            };

            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("final_migrated", totalMigrated);

            logger.LogInformation("Achievement backfill completed successfully: migrated {MigratedCount}/{TotalCount} in {DurationMs}ms",
                totalMigrated, totalCount, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Achievement backfill failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new BackfillResult
            {
                Success = false,
                TotalCount = 0,
                MigratedCount = 0,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                ExecutedAtUtc = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Backfill achievements for a specific player
    /// </summary>
    public async Task<BackfillResult> BackfillPlayerAchievementsAsync(string playerName)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var activity = ActivitySources.Backfill.StartActivity("BackfillPlayerAchievements");
            activity?.SetTag("operation", "player_backfill");
            activity?.SetTag("player_name", playerName);

            logger.LogInformation("Starting achievement backfill for player {PlayerName}", playerName);

            var achievements = await GetClickHouseAchievementsForPlayerAsync(playerName);

            if (achievements.Any())
            {
                await sqliteService.InsertAchievementsBatchAsync(achievements);
            }

            stopwatch.Stop();

            var result = new BackfillResult
            {
                Success = true,
                TotalCount = achievements.Count,
                MigratedCount = achievements.Count,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ExecutedAtUtc = DateTime.UtcNow
            };

            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("achievement_count", achievements.Count);

            logger.LogInformation("Player achievement backfill completed for {PlayerName}: {Count} achievements in {DurationMs}ms",
                playerName, achievements.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Player achievement backfill failed for {PlayerName} after {ElapsedMs}ms: {Error}",
                playerName, stopwatch.ElapsedMilliseconds, ex.Message);

            return new BackfillResult
            {
                Success = false,
                TotalCount = 0,
                MigratedCount = 0,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                ExecutedAtUtc = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Get total count of achievements in ClickHouse
    /// </summary>
    private async Task<int> GetClickHouseAchievementCountAsync()
    {
        try
        {
            if (_clickHouseConnection.State != System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.OpenAsync();
            }

            var excludeList = string.Join(", ", DeprecatedAchievementIds.Select(id => $"'{id}'"));
            var query = $"SELECT COUNT(*) FROM player_achievements_deduplicated WHERE achievement_id NOT IN ({excludeList})";

            await using var command = _clickHouseConnection.CreateCommand();
            command.CommandText = query;

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievement count from ClickHouse");
            return 0;
        }
    }

    /// <summary>
    /// Get a batch of achievements from ClickHouse
    /// </summary>
    private async Task<List<Achievement>> GetClickHouseAchievementsBatchAsync(int batchNumber, int batchSize)
    {
        try
        {
            if (_clickHouseConnection.State != System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.OpenAsync();
            }

            var offset = batchNumber * batchSize;
            var excludeList = string.Join(", ", DeprecatedAchievementIds.Select(id => $"'{id}'"));
            var query = $@"
                SELECT
                    player_name,
                    achievement_type,
                    achievement_id,
                    achievement_name,
                    tier,
                    value,
                    achieved_at,
                    processed_at,
                    server_guid,
                    map_name,
                    round_id,
                    metadata,
                    game
                FROM player_achievements_deduplicated
                WHERE achievement_id NOT IN ({excludeList})
                ORDER BY achieved_at
                LIMIT {batchSize} OFFSET {offset}";

            await using var command = _clickHouseConnection.CreateCommand();
            command.CommandText = query;

            var achievements = new List<Achievement>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var achievement = new Achievement
                {
                    PlayerName = reader.GetString(0),
                    AchievementType = reader.GetString(1),
                    AchievementId = reader.GetString(2),
                    AchievementName = reader.GetString(3),
                    Tier = reader.GetString(4),
                    Value = Convert.ToUInt32(reader.GetValue(5)),
                    AchievedAt = DateTime.Parse(reader.GetString(6)),
                    ProcessedAt = DateTime.Parse(reader.GetString(7)),
                    ServerGuid = reader.GetString(8),
                    MapName = reader.GetString(9),
                    RoundId = reader.GetString(10),
                    Metadata = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Game = reader.IsDBNull(12) ? null : reader.GetString(12)
                };

                achievements.Add(achievement);
            }

            return achievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievement batch {BatchNumber} (size: {BatchSize})", batchNumber, batchSize);
            return [];
        }
    }

    /// <summary>
    /// Get achievements for a specific player from ClickHouse
    /// </summary>
    private async Task<List<Achievement>> GetClickHouseAchievementsForPlayerAsync(string playerName)
    {
        try
        {
            if (_clickHouseConnection.State != System.Data.ConnectionState.Open)
            {
                await _clickHouseConnection.OpenAsync();
            }

            var excludeList = string.Join(", ", DeprecatedAchievementIds.Select(id => $"'{id}'"));
            var query = $@"
                SELECT
                    player_name,
                    achievement_type,
                    achievement_id,
                    achievement_name,
                    tier,
                    value,
                    achieved_at,
                    processed_at,
                    server_guid,
                    map_name,
                    round_id,
                    metadata,
                    game
                FROM player_achievements_deduplicated
                WHERE player_name = {{playerName:String}}
                  AND achievement_id NOT IN ({excludeList})
                ORDER BY achieved_at";

            await using var command = _clickHouseConnection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("playerName", playerName));

            var achievements = new List<Achievement>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var achievement = new Achievement
                {
                    PlayerName = reader.GetString(0),
                    AchievementType = reader.GetString(1),
                    AchievementId = reader.GetString(2),
                    AchievementName = reader.GetString(3),
                    Tier = reader.GetString(4),
                    Value = Convert.ToUInt32(reader.GetValue(5)),
                    AchievedAt = DateTime.Parse(reader.GetString(6)),
                    ProcessedAt = DateTime.Parse(reader.GetString(7)),
                    ServerGuid = reader.GetString(8),
                    MapName = reader.GetString(9),
                    RoundId = reader.GetString(10),
                    Metadata = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Game = reader.IsDBNull(12) ? null : reader.GetString(12)
                };

                achievements.Add(achievement);
            }

            return achievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievements for player {PlayerName}", playerName);
            return [];
        }
    }

    /// <summary>
    /// Create ClickHouse parameter
    /// </summary>
    private System.Data.Common.DbParameter CreateParameter(string name, object value)
    {
        var param = _clickHouseConnection.CreateCommand().CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        return param;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _clickHouseConnection?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of a backfill operation
/// </summary>
public class BackfillResult
{
    public bool Success { get; set; }
    public int TotalCount { get; set; }
    public int MigratedCount { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
}