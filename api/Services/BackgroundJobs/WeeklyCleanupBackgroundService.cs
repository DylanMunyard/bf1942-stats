using System.Diagnostics;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using Serilog.Context;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Executes the weekly cleanup logic. Can be triggered on-demand or by the scheduled job.
/// </summary>
public class WeeklyCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyCleanupBackgroundService> logger,
    IClock clock
) : IWeeklyCleanupBackgroundService
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("WeeklyCleanup");
        activity?.SetTag("bulk_operation", "true");
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Starting weekly cleanup");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        // Suppress EF SQL logging during bulk operations
        using (LogContext.PushProperty("bulk_operation", true))
        {
            try
            {
                var bestScoresDeleted = await CleanupThisWeekBestScoresAsync(dbContext, ct);
                var onlineCountsDeleted = await PruneOldServerOnlineCountsAsync(dbContext, ct);

                stopwatch.Stop();
                activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("result.best_scores_deleted", bestScoresDeleted);
                activity?.SetTag("result.online_counts_deleted", onlineCountsDeleted);

                logger.LogInformation(
                    "Weekly cleanup completed: deleted {BestScores} stale best scores, pruned {OnlineCounts} old server counts in {Duration}ms",
                    bestScoresDeleted, onlineCountsDeleted, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Failed to complete weekly cleanup");
                throw;
            }
        }
    }

    private async Task<int> CleanupThisWeekBestScoresAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();

        // Calculate the start of the current week (Monday 00:00 UTC)
        var today = now.InUtc().Date;
        var daysFromMonday = (int)today.DayOfWeek - (int)IsoDayOfWeek.Monday;
        var weekStart = today.Minus(Period.FromDays(daysFromMonday));
        var weekStartInstant = weekStart.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

        // Use ExecuteDeleteAsync - no need to load entries just to delete them
        return await dbContext.PlayerBestScores
            .Where(b => b.Period == "this_week" && b.RoundEndTime < weekStartInstant)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<int> PruneOldServerOnlineCountsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var retentionDays = 180;
        var cutoffTime = now.Minus(Duration.FromDays(retentionDays));

        // Use ExecuteDeleteAsync with batching via raw SQL to avoid loading into memory
        // SQLite DELETE with LIMIT requires raw SQL
        var batchSize = 10000;
        var totalDeleted = 0;
        var cutoffString = InstantPattern.ExtendedIso.Format(cutoffTime);

        while (true)
        {
            // SQLite-compatible batched delete using rowid
            var deletedInBatch = await dbContext.Database.ExecuteSqlRawAsync(
                @"DELETE FROM ServerOnlineCounts
                  WHERE rowid IN (
                      SELECT rowid FROM ServerOnlineCounts
                      WHERE HourTimestamp < @p0
                      LIMIT @p1
                  )",
                [cutoffString, batchSize],
                ct);

            if (deletedInBatch == 0)
            {
                break;
            }

            totalDeleted += deletedInBatch;

            // Small delay between batches to reduce lock contention
            if (deletedInBatch == batchSize)
            {
                await Task.Delay(100, ct);
            }
        }

        return totalDeleted;
    }
}
