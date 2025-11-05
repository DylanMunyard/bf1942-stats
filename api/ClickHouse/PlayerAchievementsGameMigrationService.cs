using Microsoft.Extensions.Logging;
using api.ClickHouse.Base;

namespace api.ClickHouse;

public class PlayerAchievementsGameMigrationService : BaseClickHouseService
{
    private readonly ILogger<PlayerAchievementsGameMigrationService> _logger;

    public PlayerAchievementsGameMigrationService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerAchievementsGameMigrationService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateToAddGameColumnAsync(
        int batchSize = 1_000_000,
        int delayMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        var totalMigrated = 0;

        try
        {
            _logger.LogInformation("Starting player_achievements migration to add game column using server_online_counts JOIN");

            // Create new table structure with game column
            await CreatePlayerAchievementsV2TableAsync();

            // Discover months to migrate
            var monthsQuery = "SELECT DISTINCT toYYYYMM(achieved_at) AS ym FROM player_achievements ORDER BY ym";
            var monthsRaw = await ExecuteQueryInternalAsync(monthsQuery);
            var months = monthsRaw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (months.Count == 0)
            {
                _logger.LogInformation("No data found in player_achievements; nothing to migrate.");
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

                // Insert month partition from player_achievements into v2 with game column via JOIN
                var migrateQuery = $@"
INSERT INTO player_achievements_v2_with_game
SELECT
  pa.player_name,
  pa.achievement_type,
  pa.achievement_id,
  pa.achievement_name,
  pa.tier,
  pa.value,
  pa.achieved_at,
  pa.processed_at,
  pa.server_guid,
  pa.map_name,
  pa.round_id,
  pa.metadata,
  pa.version,
  COALESCE(soc.game, 'unknown') as game
FROM player_achievements pa
LEFT JOIN (
    SELECT DISTINCT server_guid, game
    FROM server_online_counts
    WHERE game != ''
) soc ON pa.server_guid = soc.server_guid
WHERE toYYYYMM(pa.achieved_at) = {ym}";

                await ExecuteCommandAsync(migrateQuery);

                // Progress metrics per month
                var srcCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_achievements WHERE toYYYYMM(achieved_at) = {ym}");
                var dstCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_achievements_v2_with_game WHERE toYYYYMM(achieved_at) = {ym}");
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

            // Verify migration
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

    private async Task CreatePlayerAchievementsV2TableAsync()
    {
        // Drop and recreate to ensure schema matches new design with game column
        await ExecuteCommandAsync("DROP TABLE IF EXISTS player_achievements_v2_with_game");

        var createTableQuery = @"
CREATE TABLE player_achievements_v2_with_game
(
    player_name String,
    achievement_type String,
    achievement_id String,
    achievement_name String,
    tier String,
    value UInt32,
    achieved_at DateTime,
    processed_at DateTime,
    server_guid String,
    map_name String,
    round_id String,
    metadata String,
    version DateTime,
    game String
)
ENGINE = ReplacingMergeTree(version)
PARTITION BY toYYYYMM(achieved_at)
ORDER BY (player_name, achievement_type, achievement_id, round_id, achieved_at)";

        await ExecuteCommandAsync(createTableQuery);
        _logger.LogInformation("Created player_achievements_v2_with_game table with game column");
    }

    private async Task<bool> VerifyMigrationAsync()
    {
        try
        {
            // Compare total counts
            var oldCountQuery = "SELECT COUNT(*) FROM player_achievements";
            var newCountQuery = "SELECT COUNT(*) FROM player_achievements_v2_with_game";

            var oldCount = long.Parse((await ExecuteQueryInternalAsync(oldCountQuery)).Trim());
            var newCount = long.Parse((await ExecuteQueryInternalAsync(newCountQuery)).Trim());

            // Check game column population
            var gamePopulatedQuery = "SELECT COUNT(*) FROM player_achievements_v2_with_game WHERE game != 'unknown' AND game != ''";
            var gamePopulated = long.Parse((await ExecuteQueryInternalAsync(gamePopulatedQuery)).Trim());

            _logger.LogInformation("Verification: Old count={OldCount}, New count={NewCount}, Game populated={GamePopulated}",
                oldCount, newCount, gamePopulated);

            return oldCount == newCount && gamePopulated > 0;
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
            _logger.LogInformation("Switching tables: player_achievements -> player_achievements_backup, player_achievements_v2_with_game -> player_achievements");

            await ExecuteCommandAsync("RENAME TABLE player_achievements TO player_achievements_backup");
            await ExecuteCommandAsync("RENAME TABLE player_achievements_v2_with_game TO player_achievements");

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

            await ExecuteCommandAsync("RENAME TABLE player_achievements TO player_achievements_failed");
            await ExecuteCommandAsync("RENAME TABLE player_achievements_backup TO player_achievements");

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
            _logger.LogInformation("Dropping old backup table player_achievements_backup");

            await ExecuteCommandAsync("DROP TABLE IF EXISTS player_achievements_backup");

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
}
