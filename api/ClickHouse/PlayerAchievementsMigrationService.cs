using Microsoft.Extensions.Logging;
using api.ClickHouse.Base;

namespace api.ClickHouse;

public class PlayerAchievementsMigrationService : BaseClickHouseService
{
    private readonly ILogger<PlayerAchievementsMigrationService> _logger;

    public PlayerAchievementsMigrationService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerAchievementsMigrationService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateToReplacingMergeTreeAsync()
    {
        var startTime = DateTime.UtcNow;
        var totalMigrated = 0;

        try
        {
            _logger.LogInformation("Starting team victory achievements migration to ReplacingMergeTree for idempotency");

            // Create new table structure with ReplacingMergeTree for idempotency
            await CreatePlayerAchievementsV2TableAsync();

            // Migrate existing data
            var migrateQuery = @"
INSERT INTO player_achievements_v2
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
    processed_at as version  -- Use processed_at as version for ReplacingMergeTree
FROM player_achievements";

            await ExecuteCommandAsync(migrateQuery);

            // Get count of migrated records
            var srcCountStr = await ExecuteQueryInternalAsync("SELECT COUNT(*) FROM player_achievements");
            var dstCountStr = await ExecuteQueryInternalAsync("SELECT COUNT(*) FROM player_achievements_v2");
            var srcCount = long.Parse(srcCountStr.Trim());
            var dstCount = long.Parse(dstCountStr.Trim());

            totalMigrated = (int)dstCount;

            // Verify migration
            var verificationResult = await VerifyMigrationAsync();

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Migration completed: {TotalMigrated} rows migrated in {Duration}. Verification: {Verified}",
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
        // Drop and recreate to ensure schema matches new idempotent design
        await ExecuteCommandAsync("DROP TABLE IF EXISTS player_achievements_v2");

        var createTableQuery = @"
CREATE TABLE player_achievements_v2
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
    version DateTime  -- Version column for ReplacingMergeTree deduplication
)
ENGINE = ReplacingMergeTree(version)
PARTITION BY toYYYYMM(achieved_at)
ORDER BY (player_name, achievement_type, achievement_id, round_id, achieved_at)";

        await ExecuteCommandAsync(createTableQuery);
        _logger.LogInformation("Created player_achievements_v2 table with ReplacingMergeTree(version) for idempotency");
    }

    private async Task<bool> VerifyMigrationAsync()
    {
        try
        {
            // Compare unique achievement counts
            var oldUniqueQuery = "SELECT uniqExact(tuple(player_name, achievement_type, achievement_id, round_id, achieved_at)) FROM player_achievements";
            var newUniqueQuery = "SELECT uniqExact(tuple(player_name, achievement_type, achievement_id, round_id, achieved_at)) FROM player_achievements_v2";

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
            _logger.LogInformation("Switching tables: player_achievements -> player_achievements_backup, player_achievements_v2 -> player_achievements");

            await ExecuteCommandAsync("RENAME TABLE player_achievements TO player_achievements_backup");
            await ExecuteCommandAsync("RENAME TABLE player_achievements_v2 TO player_achievements");

            _logger.LogInformation("Table switch completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch tables");
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
