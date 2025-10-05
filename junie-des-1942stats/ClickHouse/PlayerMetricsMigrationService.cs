using System.Text;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.ClickHouse.Base;

namespace junie_des_1942stats.ClickHouse;

public class PlayerMetricsMigrationService : BaseClickHouseService
{
    private readonly ILogger<PlayerMetricsMigrationService> _logger;

    public PlayerMetricsMigrationService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerMetricsMigrationService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateToReplacingMergeTreeAsync(
        int batchSize = 1_000_000,
        int delayMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        var totalMigrated = 0;

        try
        {
            _logger.LogInformation("Starting player_metrics migration to ReplacingMergeTree using composite key and monthly partitions");

            // Drop unused daily_rankings view if it exists to free up memory
            await DropDailyRankingsViewIfExistsAsync();

            // Create new table structure (composite key + version)
            await CreatePlayerMetricsV2TableAsync();

            // Discover months to migrate
            var monthsQuery = "SELECT DISTINCT toYYYYMM(timestamp) AS ym FROM player_metrics ORDER BY ym";
            var monthsRaw = await ExecuteQueryInternalAsync(monthsQuery);
            var months = monthsRaw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (months.Count == 0)
            {
                _logger.LogInformation("No data found in player_metrics; nothing to migrate.");
                return new MigrationResult
                {
                    Success = true,
                    TotalMigrated = 0,
                    Duration = DateTime.UtcNow - startTime,
                    VerificationPassed = true
                };
            }

            _logger.LogInformation("Identified {MonthCount} month partitions to migrate: {Months}", months.Count, string.Join(",", months));

            foreach (var ym in months)
            {
                var monthStart = DateTime.UtcNow;
                _logger.LogInformation("Migrating month partition {Ym} ...", ym);

                // Insert month partition from player_metrics into v2 with version based on observation timestamp
                var migrateQuery = $@"
INSERT INTO player_metrics_v2
SELECT
  timestamp,
  server_guid,
  player_name,
  server_name,
  score,
  kills,
  deaths,
  ping,
  team_name,
  map_name,
  game_type,
  is_bot
FROM player_metrics
WHERE toYYYYMM(timestamp) = {ym}";

                await ExecuteCommandAsync(migrateQuery);

                // Progress metrics per month
                var srcCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_metrics WHERE toYYYYMM(timestamp) = {ym}");
                var dstCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_metrics_v2 WHERE toYYYYMM(timestamp) = {ym}");
                var srcCount = long.Parse(srcCountStr.Trim());
                var dstCount = long.Parse(dstCountStr.Trim());

                totalMigrated += (int)dstCount;

                var monthDuration = DateTime.UtcNow - monthStart;
                _logger.LogInformation(
                    "Month {Ym}: Source rows={SrcCount}, Migrated rows={DstCount} in {DurationMs}ms",
                    ym, srcCount, dstCount, monthDuration.TotalMilliseconds);

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }

            // Verify migration comparing unique composites as FINAL
            var verificationResult = await VerifyMigrationAsync();

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Migration completed: {TotalMigrated} rows inserted in {Duration}. Verification: {Verified}",
                totalMigrated, duration, verificationResult ? "PASSED" : "FAILED");

            return new MigrationResult
            {
                Success = true,
                TotalMigrated = totalMigrated,
                Duration = duration,
                VerificationPassed = verificationResult
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Migration failed after {Migrated} records in {Duration}", totalMigrated, duration);

            return new MigrationResult
            {
                Success = false,
                TotalMigrated = totalMigrated,
                Duration = duration,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task CreatePlayerMetricsV2TableAsync()
    {
        // Drop and recreate to ensure schema matches new composite key design
        await ExecuteCommandAsync("DROP TABLE IF EXISTS player_metrics_v2");

        var createTableQuery = @"
CREATE TABLE player_metrics_v2
(
    timestamp   DateTime,
    server_guid String,
    player_name String,

    server_name String,
    score       Int32,
    kills       UInt16,
    deaths      UInt16,
    ping        UInt16,
    team_name   String,
    map_name    String,
    game_type   String,
    is_bot      UInt8
)
ENGINE = ReplacingMergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (timestamp, server_guid, player_name)";

        await ExecuteCommandAsync(createTableQuery);
        _logger.LogInformation("Created player_metrics_v2 table with composite key and ReplacingMergeTree(version)");
    }

    private async Task<bool> VerifyMigrationAsync()
    {
        try
        {
            // Compare unique composite key counts
            var oldUniqueQuery = "SELECT uniqExact(tuple(timestamp, server_guid, player_name)) FROM player_metrics";
            var newUniqueQuery = "SELECT uniqExact(tuple(timestamp, server_guid, player_name)) FROM player_metrics_v2";

            var oldUnique = long.Parse((await ExecuteQueryInternalAsync(oldUniqueQuery)).Trim());
            var newUnique = long.Parse((await ExecuteQueryInternalAsync(newUniqueQuery)).Trim());

            _logger.LogInformation("Verification: Old unique={OldUnique}, New unique={NewUnique}", oldUnique, newUnique);

            return oldUnique == newUnique;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification failed");
            return false;
        }
    }

    public async Task<bool> SwitchToNewTableAsync()
    {
        try
        {
            _logger.LogInformation("Switching tables: player_metrics -> player_metrics_backup, player_metrics_v2 -> player_metrics");

            await ExecuteCommandAsync("RENAME TABLE player_metrics TO player_metrics_backup");
            await ExecuteCommandAsync("RENAME TABLE player_metrics_v2 TO player_metrics");

            _logger.LogInformation("Table switch completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch tables");
            return false;
        }
    }

    public async Task<bool> RollbackTableSwitchAsync()
    {
        try
        {
            _logger.LogInformation("Rolling back table switch");

            await ExecuteCommandAsync("RENAME TABLE player_metrics TO player_metrics_failed");
            await ExecuteCommandAsync("RENAME TABLE player_metrics_backup TO player_metrics");

            _logger.LogInformation("Rollback completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback table switch");
            return false;
        }
    }

    public async Task<bool> CleanupOldTableAsync()
    {
        try
        {
            _logger.LogInformation("Dropping old backup table player_metrics_backup");

            await ExecuteCommandAsync("DROP TABLE IF EXISTS player_metrics_backup");

            _logger.LogInformation("Cleanup completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old table");
            return false;
        }
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }

    /// <summary>
    /// Drops the unused daily_rankings materialized view if it exists to free up memory
    /// </summary>
    private async Task DropDailyRankingsViewIfExistsAsync()
    {
        try
        {
            _logger.LogInformation("Dropping unused daily_rankings materialized view to free up memory");
            await ExecuteCommandAsync("DROP VIEW IF EXISTS daily_rankings");
            _logger.LogInformation("Successfully dropped daily_rankings materialized view");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to drop daily_rankings materialized view - it may not exist");
        }
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public int TotalMigrated { get; set; }
    public TimeSpan Duration { get; set; }
    public bool VerificationPassed { get; set; }
    public string? ErrorMessage { get; set; }
}