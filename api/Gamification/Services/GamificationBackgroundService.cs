using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public class GamificationBackgroundService(IServiceProvider services, ILogger<GamificationBackgroundService> logger) : BackgroundService
{

    // Check environment variable for gamification processing - default to false (disabled)
    private readonly bool _enableGamificationProcessing = Environment.GetEnvironmentVariable("ENABLE_GAMIFICATION_PROCESSING")?.ToLowerInvariant() == "true";

    // Log configuration in static constructor-like fashion would require a different approach
    // We'll log it in ExecuteAsync instead to avoid issues with logging in field initializers

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Gamification processing: {Status}", _enableGamificationProcessing ? "ENABLED" : "DISABLED");
        logger.LogInformation("To enable gamification processing: Set ENABLE_GAMIFICATION_PROCESSING=true");
        logger.LogInformation("Gamification background service started");

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
