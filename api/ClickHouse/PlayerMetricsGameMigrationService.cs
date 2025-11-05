using Microsoft.Extensions.Logging;
using api.ClickHouse.Base;

namespace api.ClickHouse;

public class PlayerMetricsGameMigrationService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerMetricsGameMigrationService> logger) : BaseClickHouseService(httpClient, clickHouseUrl)
{

    public async Task<MigrationResult> MigrateToAddGameColumnAsync(
        int batchSize = 1_000_000,
        int delayMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        var totalMigrated = 0;

        try
        {
            logger.LogInformation("Starting player_metrics migration to add game column using server_online_counts JOIN");

            // Create new table structure with game column
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
                logger.LogInformation("No data found in player_metrics; nothing to migrate.");
                return new MigrationResult
                {
                    Success = true,
                    TotalMigrated = 0,
                    Duration = DateTime.UtcNow - startTime,
                    VerificationPassed = true
                };
            }

            logger.LogInformation("Identified {MonthCount} month partitions to migrate: {Months}", months.Count, string.Join(",", months));

            foreach (var ym in months)
            {
                var monthStart = DateTime.UtcNow;
                logger.LogInformation("Migrating month partition {Ym} ...", ym);

                // Insert month partition from player_metrics into v2 with game column via JOIN
                var migrateQuery = $@"
INSERT INTO player_metrics_v2
SELECT
  pm.timestamp,
  pm.server_guid,
  pm.player_name,
  pm.server_name,
  pm.score,
  pm.kills,
  pm.deaths,
  pm.ping,
  pm.team_name,
  pm.map_name,
  pm.game_type,
  pm.is_bot,
  COALESCE(soc.game, 'unknown') as game
FROM player_metrics pm
LEFT JOIN (
    SELECT DISTINCT server_guid, game
    FROM server_online_counts
    WHERE game != ''
) soc ON pm.server_guid = soc.server_guid
WHERE toYYYYMM(pm.timestamp) = {ym}";

                await ExecuteCommandAsync(migrateQuery);

                // Progress metrics per month
                var srcCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_metrics WHERE toYYYYMM(timestamp) = {ym}");
                var dstCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_metrics_v2 WHERE toYYYYMM(timestamp) = {ym}");
                var srcCount = long.Parse(srcCountStr.Trim());
                var dstCount = long.Parse(dstCountStr.Trim());

                totalMigrated += (int)dstCount;

                var monthDuration = DateTime.UtcNow - monthStart;
                logger.LogInformation(
                    "Month {Ym}: Source rows={SrcCount}, Migrated rows={DstCount} in {DurationMs}ms",
                    ym, srcCount, dstCount, monthDuration.TotalMilliseconds);

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }

            // Verify migration
            var verificationResult = await VerifyMigrationAsync();

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation(
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
            logger.LogError(ex, "Migration failed after {Migrated} records in {Duration}", totalMigrated, duration);

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
        // Drop and recreate to ensure schema matches new design with game column
        await ExecuteCommandAsync("DROP TABLE IF EXISTS player_metrics_v2");

        var createTableQuery = @"
CREATE TABLE player_metrics_v2
(
    timestamp DateTime,
    server_guid String,
    player_name String,
    server_name String,
    score Int32,
    kills UInt16,
    deaths UInt16,
    ping UInt16,
    team_name String,
    map_name String,
    game_type String,
    is_bot UInt8,
    game String
)
ENGINE = ReplacingMergeTree
PARTITION BY toYYYYMM(timestamp)
ORDER BY (timestamp, server_guid, player_name)
SETTINGS index_granularity = 8192";

        await ExecuteCommandAsync(createTableQuery);
        logger.LogInformation("Created player_metrics_v2 table with game column");
    }

    private async Task<bool> VerifyMigrationAsync()
    {
        try
        {
            // Compare total counts
            var oldCountQuery = "SELECT COUNT(*) FROM player_metrics";
            var newCountQuery = "SELECT COUNT(*) FROM player_metrics_v2";

            var oldCount = long.Parse((await ExecuteQueryInternalAsync(oldCountQuery)).Trim());
            var newCount = long.Parse((await ExecuteQueryInternalAsync(newCountQuery)).Trim());

            // Check game column population
            var gamePopulatedQuery = "SELECT COUNT(*) FROM player_metrics_v2 WHERE game != 'unknown' AND game != ''";
            var gamePopulated = long.Parse((await ExecuteQueryInternalAsync(gamePopulatedQuery)).Trim());

            logger.LogInformation("Verification: Old count={OldCount}, New count={NewCount}, Game populated={GamePopulated}",
                oldCount, newCount, gamePopulated);

            return oldCount == newCount && gamePopulated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Verification failed");
            return false;
        }
    }

    public async Task<bool> SwitchToNewTableAsync()
    {
        try
        {
            logger.LogInformation("Switching tables: player_metrics -> player_metrics_backup, player_metrics_v2 -> player_metrics");

            await ExecuteCommandAsync("RENAME TABLE player_metrics TO player_metrics_backup");
            await ExecuteCommandAsync("RENAME TABLE player_metrics_v2 TO player_metrics");

            logger.LogInformation("Table switch completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to switch tables");
            return false;
        }
    }

    public async Task<bool> RollbackTableSwitchAsync()
    {
        try
        {
            logger.LogInformation("Rolling back table switch");

            await ExecuteCommandAsync("RENAME TABLE player_metrics TO player_metrics_failed");
            await ExecuteCommandAsync("RENAME TABLE player_metrics_backup TO player_metrics");

            logger.LogInformation("Rollback completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rollback table switch");
            return false;
        }
    }

    public async Task<bool> CleanupOldTableAsync()
    {
        try
        {
            logger.LogInformation("Dropping old backup table player_metrics_backup");

            await ExecuteCommandAsync("DROP TABLE IF EXISTS player_metrics_backup");

            logger.LogInformation("Cleanup completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup old table");
            return false;
        }
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }
}
