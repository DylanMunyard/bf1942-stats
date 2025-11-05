using Microsoft.Extensions.Logging;
using api.ClickHouse;

namespace api.Data.Migrations;

public class PlayerAchievementsMigrationController(PlayerAchievementsMigrationService migrationService, ILogger<PlayerAchievementsMigrationController> logger)
{
    private readonly PlayerAchievementsMigrationService _migrationService = migrationService;
    private readonly ILogger<PlayerAchievementsMigrationController> _logger = logger;

    public class MigrationResponse
    {
        public bool Success { get; set; }
        public int TotalMigrated { get; set; }
        public long DurationMs { get; set; }
        public bool VerificationPassed { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
    }

    public class SwitchResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public string Operation { get; set; } = "";
    }

    /// <summary>
    /// Migrates player_achievements table from MergeTree to ReplacingMergeTree engine for idempotency.
    /// This creates a new table (player_achievements_v2) and migrates all data with version column.
    /// </summary>
    public async Task<MigrationResponse> MigrateToReplacingMergeTree()
    {
        _logger.LogInformation("Player achievements migration request started");

        var result = await _migrationService.MigrateToReplacingMergeTreeAsync();

        var ended = DateTime.UtcNow;
        var response = new MigrationResponse
        {
            Success = result.Success,
            TotalMigrated = result.TotalMigrated,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            VerificationPassed = result.VerificationPassed,
            ErrorMessage = result.ErrorMessage,
            ExecutedAtUtc = ended
        };

        if (result.Success)
        {
            _logger.LogInformation(
                "Player achievements migration completed successfully: migrated={Count} durationMs={DurationMs} verified={Verified}",
                response.TotalMigrated, response.DurationMs, response.VerificationPassed);
        }
        else
        {
            _logger.LogError(
                "Player achievements migration failed: migrated={Count} durationMs={DurationMs} error={Error}",
                response.TotalMigrated, response.DurationMs, response.ErrorMessage);
        }

        return response;
    }

    /// <summary>
    /// Switches the tables: player_achievements -> player_achievements_backup, player_achievements_v2 -> player_achievements.
    /// This should only be called AFTER migration is complete and verified, and AFTER application code is updated.
    /// </summary>
    public async Task<SwitchResponse> SwitchToNewTable()
    {
        _logger.LogInformation("Player achievements table switch request started");

        var success = await _migrationService.SwitchToNewTableAsync();

        var ended = DateTime.UtcNow;
        var response = new SwitchResponse
        {
            Success = success,
            ErrorMessage = success ? null : "Failed to switch tables - check logs for details",
            ExecutedAtUtc = ended,
            Operation = "switch"
        };

        if (success)
        {
            _logger.LogInformation("Player achievements table switch completed successfully");
        }
        else
        {
            _logger.LogError("Player achievements table switch failed");
        }

        return response;
    }

    /// <summary>
    /// Cleans up the old backup table after migration is verified successful.
    /// This should only be called after you're confident the migration worked correctly.
    /// </summary>
    public async Task<SwitchResponse> CleanupOldTable()
    {
        _logger.LogInformation("Player achievements table cleanup request started");

        var success = await _migrationService.CleanupOldTableAsync();

        var ended = DateTime.UtcNow;
        var response = new SwitchResponse
        {
            Success = success,
            ErrorMessage = success ? null : "Failed to cleanup old table - check logs for details",
            ExecutedAtUtc = ended,
            Operation = "cleanup"
        };

        if (success)
        {
            _logger.LogInformation("Player achievements table cleanup completed successfully");
        }
        else
        {
            _logger.LogError("Player achievements table cleanup failed");
        }

        return response;
    }
}
