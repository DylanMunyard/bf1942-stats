using api.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

/// <summary>
/// Admin endpoints for triggering background jobs on-demand.
/// Useful for testing and debugging during development.
/// </summary>
[ApiController]
[Route("stats/admin/jobs")]
[Authorize(Policy = "Admin")]
public class AdminJobsController(
    IDailyAggregateRefreshBackgroundService dailyAggregateRunner,
    IWeeklyCleanupBackgroundService weeklyCleanupRunner,
    IAggregateBackfillBackgroundService aggregateBackfillRunner,
    ILogger<AdminJobsController> logger
) : ControllerBase
{
    /// <summary>
    /// Trigger the daily aggregate refresh job.
    /// Refreshes: ServerHourlyPatterns, HourlyPlayerPredictions, MapGlobalAverages
    /// </summary>
    [HttpPost("daily-aggregate-refresh")]
    public async Task<IActionResult> TriggerDailyAggregateRefresh(CancellationToken ct)
    {
        logger.LogInformation("Manual trigger: DailyAggregateRefresh");

        try
        {
            await dailyAggregateRunner.RunAsync(ct);
            return Ok(new { message = "Daily aggregate refresh completed successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run DailyAggregateRefresh");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Trigger the weekly cleanup job.
    /// Removes stale "this_week" best scores and prunes old ServerOnlineCounts.
    /// </summary>
    [HttpPost("weekly-cleanup")]
    public async Task<IActionResult> TriggerWeeklyCleanup(CancellationToken ct)
    {
        logger.LogInformation("Manual trigger: WeeklyCleanup");

        try
        {
            await weeklyCleanupRunner.RunAsync(ct);
            return Ok(new { message = "Weekly cleanup completed successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run WeeklyCleanup");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Trigger aggregate backfill for a specific tier (fire-and-forget).
    /// Tier 1: Players active within 7 days (prioritized)
    /// Tier 2: Players active within 30 days
    /// Tier 3: Players active within 90 days
    /// Tier 4: All remaining players
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("aggregate-backfill/{tier:int}")]
    public IActionResult TriggerAggregateBackfillTier(int tier)
    {
        if (tier < 1 || tier > 4)
        {
            return BadRequest(new { error = "Tier must be between 1 and 4" });
        }

        logger.LogInformation("Manual trigger: AggregateBackfill tier {Tier} (fire-and-forget)", tier);

        _ = Task.Run(async () =>
        {
            try
            {
                await aggregateBackfillRunner.RunTierAsync(tier);
                logger.LogInformation("AggregateBackfill tier {Tier} completed successfully", tier);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AggregateBackfill tier {Tier} failed", tier);
            }
        });

        return Accepted(new { message = $"Aggregate backfill tier {tier} started in background. Check logs for progress." });
    }

    /// <summary>
    /// Trigger full aggregate backfill (all tiers) - fire-and-forget.
    /// This is a long-running operation that processes all historical data.
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("aggregate-backfill")]
    public IActionResult TriggerAggregateBackfill()
    {
        logger.LogInformation("Manual trigger: AggregateBackfill (all tiers, fire-and-forget)");

        _ = Task.Run(async () =>
        {
            try
            {
                await aggregateBackfillRunner.RunAsync();
                logger.LogInformation("Full aggregate backfill completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Full aggregate backfill failed");
            }
        });

        return Accepted(new { message = "Full aggregate backfill started in background. Check logs for progress." });
    }

    /// <summary>
    /// Trigger full ServerMapStats backfill from all historical Rounds data (fire-and-forget).
    /// Use this for initial population - daily refresh only updates last 2 months.
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("server-map-stats-backfill")]
    public IActionResult TriggerServerMapStatsBackfill()
    {
        logger.LogInformation("Manual trigger: ServerMapStats full backfill (fire-and-forget)");

        _ = Task.Run(async () =>
        {
            try
            {
                await dailyAggregateRunner.BackfillServerMapStatsAsync();
                logger.LogInformation("ServerMapStats full backfill completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ServerMapStats full backfill failed");
            }
        });

        return Accepted(new { message = "ServerMapStats full backfill started in background. Check logs for progress." });
    }

    /// <summary>
    /// Trigger full MapHourlyPatterns backfill from all historical Rounds data (fire-and-forget).
    /// Use this for initial population - daily refresh only updates last 60 days.
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("map-hourly-patterns-backfill")]
    public IActionResult TriggerMapHourlyPatternsBackfill()
    {
        logger.LogInformation("Manual trigger: MapHourlyPatterns full backfill (fire-and-forget)");

        _ = Task.Run(async () =>
        {
            try
            {
                await dailyAggregateRunner.BackfillMapHourlyPatternsAsync();
                logger.LogInformation("MapHourlyPatterns full backfill completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MapHourlyPatterns full backfill failed");
            }
        });

        return Accepted(new { message = "MapHourlyPatterns full backfill started in background. Check logs for progress." });
    }

    /// <summary>
    /// Trigger all background jobs in sequence (fire-and-forget).
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("run-all")]
    public IActionResult TriggerAllJobs()
    {
        logger.LogInformation("Manual trigger: All jobs (fire-and-forget)");

        _ = Task.Run(async () =>
        {
            try
            {
                await dailyAggregateRunner.RunAsync();
                logger.LogInformation("DailyAggregateRefresh completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DailyAggregateRefresh failed");
            }

            try
            {
                await weeklyCleanupRunner.RunAsync();
                logger.LogInformation("WeeklyCleanup completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WeeklyCleanup failed");
            }

            logger.LogInformation("All jobs run completed");
        });

        return Accepted(new { message = "All jobs started in background. Check logs for progress." });
    }
}
