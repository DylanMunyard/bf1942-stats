using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using api.ClickHouse;

namespace api.Data.Migrations;

[ApiController]
[Route("stats/admin/[controller]")]
public class PlayerMetricsMigrationController : ControllerBase
{
    private readonly PlayerMetricsMigrationService _migrationService;
    private readonly ILogger<PlayerMetricsMigrationController> _logger;

    public PlayerMetricsMigrationController(
        PlayerMetricsMigrationService migrationService,
        ILogger<PlayerMetricsMigrationController> logger)
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
    /// Migrates player_metrics table from MergeTree to ReplacingMergeTree engine.
    /// This creates a new table (player_metrics_v2) and migrates all data with generated metric_ids.
    /// </summary>
    [HttpPost("migrate")]
    public async Task<ActionResult<MigrationResponse>> MigrateToReplacingMergeTree(
        [FromBody] MigrationRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "PlayerMetricsMigrationController is disabled. Returning 401 Unauthorized. Request: batchSize={BatchSize} delayMs={DelayMs}",
            request.BatchSize, request.DelayMs);

        return StatusCode(401, new
        {
            message = "PlayerMetricsMigrationController is disabled and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
        var started = DateTime.UtcNow;
        _logger.LogInformation(
            "API migration request started: batchSize={BatchSize} delayMs={DelayMs}", 
            request.BatchSize, request.DelayMs);

        var result = await _migrationService.MigrateToReplacingMergeTreeAsync(
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
                "API migration completed successfully: migrated={Count} durationMs={DurationMs} verified={Verified}",
                response.TotalMigrated, response.DurationMs, response.VerificationPassed);
        }
        else
        {
            _logger.LogError(
                "API migration failed: migrated={Count} durationMs={DurationMs} error={Error}",
                response.TotalMigrated, response.DurationMs, response.ErrorMessage);
        }

        return Ok(response);
        */
    }

    /// <summary>
    /// Switches the tables: player_metrics -> player_metrics_backup, player_metrics_v2 -> player_metrics.
    /// This should only be called AFTER migration is complete and verified, and AFTER application code is updated.
    /// </summary>
    [HttpPost("switch")]
    public async Task<ActionResult<SwitchResponse>> SwitchToNewTable()
    {
        _logger.LogWarning("PlayerMetricsMigrationController is disabled. Returning 401 Unauthorized.");

        return StatusCode(401, new
        {
            message = "PlayerMetricsMigrationController is disabled and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
        var started = DateTime.UtcNow;
        _logger.LogInformation("API table switch request started");

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
            _logger.LogInformation("API table switch completed successfully");
        }
        else
        {
            _logger.LogError("API table switch failed");
        }

        return Ok(response);
        */
    }

    /// <summary>
    /// Rollback the table switch if there are issues after switching.
    /// This restores player_metrics_backup -> player_metrics and moves the new table to player_metrics_failed.
    /// </summary>
    [HttpPost("rollback")]
    public async Task<ActionResult<SwitchResponse>> RollbackTableSwitch()
    {
        _logger.LogWarning("PlayerMetricsMigrationController is disabled. Returning 401 Unauthorized.");

        return StatusCode(401, new
        {
            message = "PlayerMetricsMigrationController is disabled and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
        var started = DateTime.UtcNow;
        _logger.LogInformation("API table rollback request started");

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
            _logger.LogInformation("API table rollback completed successfully");
        }
        else
        {
            _logger.LogError("API table rollback failed");
        }

        return Ok(response);
        */
    }

    /// <summary>
    /// Cleans up the old backup table after migration is verified successful.
    /// This should only be called after you're confident the migration worked correctly.
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<ActionResult<SwitchResponse>> CleanupOldTable()
    {
        _logger.LogWarning("PlayerMetricsMigrationController is disabled. Returning 401 Unauthorized.");

        return StatusCode(401, new
        {
            message = "PlayerMetricsMigrationController is disabled and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
        var started = DateTime.UtcNow;
        _logger.LogInformation("API table cleanup request started");

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
            _logger.LogInformation("API table cleanup completed successfully");
        }
        else
        {
            _logger.LogError("API table cleanup failed");
        }

        return Ok(response);
        */
    }
}