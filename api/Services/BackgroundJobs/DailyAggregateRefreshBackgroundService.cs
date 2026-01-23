using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Background service that schedules and runs the daily aggregate refresh at 4 AM UTC.
/// </summary>
public class DailyAggregateRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyAggregateRefreshBackgroundService> logger
) : BackgroundService
{
    private static readonly TimeSpan RunTime = TimeSpan.FromHours(4); // 4 AM UTC
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DailyAggregateRefreshJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var targetToday = now.Date.Add(RunTime);

                // If we've passed today's run time, schedule for tomorrow
                var nextRun = now >= targetToday
                    ? targetToday.AddDays(1)
                    : targetToday;

                var delay = nextRun - now;
                logger.LogInformation("Next daily aggregate refresh scheduled for {NextRun} (in {Delay})",
                    nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IDailyAggregateRefreshBackgroundService>();
                await runner.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in DailyAggregateRefreshJob");
                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        logger.LogInformation("DailyAggregateRefreshJob stopped");
    }
}
