using Microsoft.Extensions.Logging;
using api.ClickHouse;

namespace api.Data.Migrations;

public class PlayerRoundsMigrationController
{
    private readonly PlayerRoundsMigrationService _migrationService;
    private readonly ILogger<PlayerRoundsMigrationController> _logger;

    public PlayerRoundsMigrationController(
        PlayerRoundsMigrationService migrationService,
        ILogger<PlayerRoundsMigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    public class MigrationRequest
    {
        public int BatchSize { get; set; } = 1_000_000;
        public int DelayMs { get; set; } = 5000;
    }

    public class MigrationResponse
    {
        public bool Success { get; set; }
        public int TotalMigrated { get; set; }
        public long DurationMs { get; set; }
        public bool VerificationPassed { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public int BatchSize { get; set; }
        public int DelayMs { get; set; }
    }

    public class SwitchResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public string Operation { get; set; } = "";
    }

    /// <summary>
    /// Migrates player_rounds table to add game column using JOINs with server_online_counts.
    /// This creates a new table (player_rounds_v2) and migrates all data with game information.
    /// </summary>
    public async Task<MigrationResponse> MigrateToAddGameColumn(
        MigrationRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        _logger.LogInformation(
            "Migration request started: batchSize={BatchSize} delayMs={DelayMs}",
            request.BatchSize, request.DelayMs);

        var result = await _migrationService.MigrateToAddGameColumnAsync(
            request.BatchSize,
            request.DelayMs);

        var ended = DateTime.UtcNow;
        var response = new MigrationResponse
        {
            Success = result.Success,
            TotalMigrated = result.TotalMigrated,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            VerificationPassed = result.VerificationPassed,
            ErrorMessage = result.ErrorMessage,
            ExecutedAtUtc = ended,
            BatchSize = request.BatchSize,
            DelayMs = request.DelayMs
        };

        if (result.Success)
        {
            _logger.LogInformation(
                "Migration completed successfully: migrated={Count} durationMs={DurationMs} verified={Verified}",
                response.TotalMigrated, response.DurationMs, response.VerificationPassed);
        }
        else
        {
            _logger.LogError(
                "Migration failed: migrated={Count} durationMs={DurationMs} error={Error}",
                response.TotalMigrated, response.DurationMs, response.ErrorMessage);
        }

        return response;
    }

    /// <summary>
    /// Switches the tables: player_rounds -> player_rounds_backup, player_rounds_v2 -> player_rounds.
    /// This should only be called AFTER migration is complete and verified, and AFTER application code is updated.
    /// </summary>
    public async Task<SwitchResponse> SwitchToNewTable()
    {
        var started = DateTime.UtcNow;
        _logger.LogInformation("Table switch request started");

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
            _logger.LogInformation("Table switch completed successfully");
        }
        else
        {
            _logger.LogError("Table switch failed");
        }

        return response;
    }

    /// <summary>
    /// Rollback the table switch if there are issues after switching.
    /// This restores player_rounds_backup -> player_rounds and moves the new table to player_rounds_failed.
    /// </summary>
    public async Task<SwitchResponse> RollbackTableSwitch()
    {
        var started = DateTime.UtcNow;
        _logger.LogInformation("Table rollback request started");

        var success = await _migrationService.RollbackTableSwitchAsync();

        var ended = DateTime.UtcNow;
        var response = new SwitchResponse
        {
            Success = success,
            ErrorMessage = success ? null : "Failed to rollback tables - check logs for details",
            ExecutedAtUtc = ended,
            Operation = "rollback"
        };

        if (success)
        {
            _logger.LogInformation("Table rollback completed successfully");
        }
        else
        {
            _logger.LogError("Table rollback failed");
        }

        return response;
    }

    /// <summary>
    /// Cleans up the old backup table after migration is verified successful.
    /// This should only be called after you're confident the migration worked correctly.
    /// </summary>
    public async Task<SwitchResponse> CleanupOldTable()
    {
        var started = DateTime.UtcNow;
        _logger.LogInformation("Table cleanup request started");

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
            _logger.LogInformation("Table cleanup completed successfully");
        }
        else
        {
            _logger.LogError("Table cleanup failed");
        }

        return response;
    }
}
