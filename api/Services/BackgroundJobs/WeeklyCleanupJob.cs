using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Background service that runs weekly on Mondays at 3 AM UTC.
/// </summary>
public class WeeklyCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyCleanupJob> logger
) : BackgroundService
{
    private static readonly TimeSpan RunTime = TimeSpan.FromHours(3); // 3 AM UTC on Mondays
    private static readonly DayOfWeek RunDay = DayOfWeek.Monday;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Clear any inherited activity context from hosting startup to prevent
        // background job traces from being correlated with unrelated HTTP requests.
        Activity.Current = null;

        logger.LogInformation("WeeklyCleanupJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = GetNextRunTime(now);
                var delay = nextRun - now;

                logger.LogInformation("Next weekly cleanup scheduled for {NextRun} (in {Delay})",
                    nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IWeeklyCleanupBackgroundService>();
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in WeeklyCleanupJob");
                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        logger.LogInformation("WeeklyCleanupJob stopped");
    }

    private static DateTime GetNextRunTime(DateTime now)
    {
        // Find the next Monday at 3 AM UTC
        var daysUntilMonday = ((int)RunDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && now.TimeOfDay >= RunTime)
        {
            daysUntilMonday = 7; // Already passed Monday's run time, schedule for next week
        }

        return now.Date.AddDays(daysUntilMonday).Add(RunTime);
    }
}
