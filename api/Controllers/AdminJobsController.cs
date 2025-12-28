using api.Gamification.Services;
using api.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
    IServerOnlineCountsBackfillBackgroundService serverOnlineCountsBackfillRunner,
    IServiceScopeFactory scopeFactory,
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
    /// Trigger ServerOnlineCounts backfill from ClickHouse to SQLite (fire-and-forget).
    /// Aggregates minute-level data to hourly granularity.
    /// Returns immediately - check logs for progress.
    /// </summary>
    /// <param name="days">Number of days to backfill (default 60)</param>
    [HttpPost("server-online-counts-backfill")]
    public IActionResult TriggerServerOnlineCountsBackfill([FromQuery] int days = 60)
    {
        if (days < 1 || days > 365)
        {
            return BadRequest(new { error = "Days must be between 1 and 365" });
        }

        logger.LogInformation("Manual trigger: ServerOnlineCountsBackfill for {Days} days (fire-and-forget)", days);

        _ = Task.Run(async () =>
        {
            try
            {
                await serverOnlineCountsBackfillRunner.RunAsync(days);
                logger.LogInformation("ServerOnlineCountsBackfill ({Days} days) completed successfully", days);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ServerOnlineCountsBackfill failed");
            }
        });

        return Accepted(new { message = $"ServerOnlineCounts backfill ({days} days) started in background. Check logs for progress." });
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
    /// Trigger full achievement backfill from ClickHouse to SQLite (fire-and-forget).
    /// Migrates all player achievements to the new SQLite storage.
    /// Returns immediately - check logs for progress.
    /// </summary>
    [HttpPost("achievement-backfill")]
    public IActionResult TriggerAchievementBackfill()
    {
        logger.LogInformation("Manual trigger: AchievementBackfill (fire-and-forget)");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedRunner = scope.ServiceProvider.GetRequiredService<AchievementBackfillService>();

            try
            {
                var result = await scopedRunner.BackfillAllAchievementsAsync();
                if (result.Success)
                {
                    logger.LogInformation("AchievementBackfill completed successfully: {Migrated} achievements in {DurationMs}ms",
                        result.MigratedCount, result.DurationMs);
                }
                else
                {
                    logger.LogError("AchievementBackfill failed: {Error}", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AchievementBackfill failed");
            }
        });

        return Accepted(new { message = "Achievement backfill started in background. Check logs for progress." });
    }

    /// <summary>
    /// Trigger achievement backfill for a specific player (fire-and-forget).
    /// Migrates all achievements for the specified player to SQLite.
    /// Returns immediately - check logs for progress.
    /// </summary>
    /// <param name="playerName">The player name to backfill achievements for</param>
    [HttpPost("achievement-backfill/player/{playerName}")]
    public IActionResult TriggerPlayerAchievementBackfill(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return BadRequest(new { error = "Player name is required" });
        }

        logger.LogInformation("Manual trigger: AchievementBackfill for player {PlayerName} (fire-and-forget)", playerName);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedRunner = scope.ServiceProvider.GetRequiredService<AchievementBackfillService>();

            try
            {
                var result = await scopedRunner.BackfillPlayerAchievementsAsync(playerName);
                if (result.Success)
                {
                    logger.LogInformation("AchievementBackfill for player {PlayerName} completed: {Migrated} achievements in {DurationMs}ms",
                        playerName, result.MigratedCount, result.DurationMs);
                }
                else
                {
                    logger.LogError("AchievementBackfill for player {PlayerName} failed: {Error}", playerName, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AchievementBackfill for player {PlayerName} failed", playerName);
            }
        });

        return Accepted(new { message = $"Achievement backfill for player '{playerName}' started in background. Check logs for progress." });
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
