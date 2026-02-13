using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public class GamificationBackgroundService(IServiceProvider services, ILogger<GamificationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    // Check environment variable for gamification processing - default to false (disabled)
    private readonly bool _enableGamificationProcessing = Environment.GetEnvironmentVariable("ENABLE_GAMIFICATION_PROCESSING")?.ToLowerInvariant() == "true";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Clear any inherited activity context from hosting startup to prevent
        // background job traces from being correlated with unrelated HTTP requests.
        Activity.Current = null;

        logger.LogInformation("Gamification processing: {Status}", _enableGamificationProcessing ? "ENABLED" : "DISABLED");

        if (!_enableGamificationProcessing)
        {
            logger.LogInformation("Gamification processing is disabled - service will remain idle");

            // Keep the service running but idle
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            return;
        }

        logger.LogInformation("GamificationBackgroundService waiting {Delay} before first run", StartupDelay);

        // Delay startup to avoid blocking Kestrel initialization
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var gamificationService = scope.ServiceProvider.GetRequiredService<GamificationService>();

                // Process new achievements every 5 minutes
                await gamificationService.ProcessNewAchievementsAsync();

                logger.LogDebug("Completed gamification processing cycle");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during gamification processing cycle");
            }

            // Wait 5 minutes before next processing
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }

        logger.LogInformation("Gamification background service stopped");
    }
}
