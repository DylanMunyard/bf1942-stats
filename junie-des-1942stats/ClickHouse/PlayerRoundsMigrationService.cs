using System.Text;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.ClickHouse.Base;

namespace junie_des_1942stats.ClickHouse;

public class PlayerRoundsMigrationService : BaseClickHouseService
{
    private readonly ILogger<PlayerRoundsMigrationService> _logger;
    
    public PlayerRoundsMigrationService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerRoundsMigrationService> logger)
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
            _logger.LogInformation("Starting player_rounds migration to add game column using server_online_counts JOIN");

            // Create new table structure with game column
            await CreatePlayerRoundsV2TableAsync();

            // Discover months to migrate
            var monthsQuery = "SELECT DISTINCT toYYYYMM(round_start_time) AS ym FROM player_rounds ORDER BY ym";
            var monthsRaw = await ExecuteQueryInternalAsync(monthsQuery);
            var months = monthsRaw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (months.Count == 0)
            {
                _logger.LogInformation("No data found in player_rounds; nothing to migrate.");
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

                // Insert month partition from player_rounds into v2 with game column via JOIN
                var migrateQuery = $@"
INSERT INTO player_rounds_v2
SELECT
  pr.player_name,
  pr.server_guid,
  pr.map_name,
  pr.round_start_time,
  pr.round_end_time,
  pr.final_score,
  pr.final_kills,
  pr.final_deaths,
  pr.play_time_minutes,
  pr.round_id,
  pr.team_label,
  pr.game_id,
  pr.created_at,
  pr.is_bot,
  COALESCE(soc.game, 'unknown') as game
FROM player_rounds pr
LEFT JOIN (
    SELECT DISTINCT server_guid, game
    FROM server_online_counts
    WHERE game != ''
) soc ON pr.server_guid = soc.server_guid
WHERE toYYYYMM(pr.round_start_time) = {ym}";

                await ExecuteCommandAsync(migrateQuery);

                // Progress metrics per month
                var srcCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_rounds WHERE toYYYYMM(round_start_time) = {ym}");
                var dstCountStr = await ExecuteQueryInternalAsync($"SELECT COUNT(*) FROM player_rounds_v2 WHERE toYYYYMM(round_start_time) = {ym}");
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

    private async Task CreatePlayerRoundsV2TableAsync()
    {
        // Drop and recreate to ensure schema matches new design with game column
        await ExecuteCommandAsync("DROP TABLE IF EXISTS player_rounds_v2");

        var createTableQuery = @"
CREATE TABLE player_rounds_v2
(
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
    created_at DateTime,
    is_bot UInt8 DEFAULT 0,
    game String DEFAULT 'unknown',
    INDEX idx_player_time (player_name, round_start_time) TYPE minmax GRANULARITY 1,
    INDEX idx_time_player (round_start_time, player_name) TYPE bloom_filter GRANULARITY 1
)
ENGINE = ReplacingMergeTree(created_at)
PARTITION BY toYYYYMM(round_start_time)
ORDER BY (player_name, server_guid, round_start_time, round_id)
SETTINGS index_granularity = 8192";

        await ExecuteCommandAsync(createTableQuery);
        _logger.LogInformation("Created player_rounds_v2 table with game column");
    }

    private async Task<bool> VerifyMigrationAsync()
    {
        try
        {
            // Compare total counts
            var oldCountQuery = "SELECT COUNT(*) FROM player_rounds";
            var newCountQuery = "SELECT COUNT(*) FROM player_rounds_v2";

            var oldCount = long.Parse((await ExecuteQueryInternalAsync(oldCountQuery)).Trim());
            var newCount = long.Parse((await ExecuteQueryInternalAsync(newCountQuery)).Trim());

            // Check game column population
            var gamePopulatedQuery = "SELECT COUNT(*) FROM player_rounds_v2 WHERE game != 'unknown' AND game != ''";
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
            _logger.LogInformation("Switching tables: player_rounds -> player_rounds_backup, player_rounds_v2 -> player_rounds");
            
            await ExecuteCommandAsync("RENAME TABLE player_rounds TO player_rounds_backup");
            await ExecuteCommandAsync("RENAME TABLE player_rounds_v2 TO player_rounds");
            
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
            
            await ExecuteCommandAsync("RENAME TABLE player_rounds TO player_rounds_failed");
            await ExecuteCommandAsync("RENAME TABLE player_rounds_backup TO player_rounds");
            
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
            _logger.LogInformation("Dropping old backup table player_rounds_backup");
            
            await ExecuteCommandAsync("DROP TABLE IF EXISTS player_rounds_backup");
            
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