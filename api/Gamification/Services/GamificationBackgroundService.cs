using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public class GamificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<GamificationBackgroundService> _logger;
    private readonly bool _enableGamificationProcessing;

    public GamificationBackgroundService(IServiceProvider services, ILogger<GamificationBackgroundService> logger)
    {
        _services = services;
        _logger = logger;

        // Check environment variable for gamification processing - default to false (disabled)
        _enableGamificationProcessing = Environment.GetEnvironmentVariable("ENABLE_GAMIFICATION_PROCESSING")?.ToLowerInvariant() == "true";

        _logger.LogInformation("Gamification processing: {Status}", _enableGamificationProcessing ? "ENABLED" : "DISABLED");
        _logger.LogInformation("To enable gamification processing: Set ENABLE_GAMIFICATION_PROCESSING=true");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gamification background service started");

        if (!_enableGamificationProcessing)
        {
            _logger.LogInformation("Gamification processing is disabled - service will remain idle");

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
                using var scope = _services.CreateScope();
                var gamificationService = scope.ServiceProvider.GetRequiredService<GamificationService>();

                // Process new achievements every 5 minutes
                await gamificationService.ProcessNewAchievementsAsync();

                _logger.LogDebug("Completed gamification processing cycle");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during gamification processing cycle");
            }

            // Wait 5 minutes before next processing
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }

        _logger.LogInformation("Gamification background service stopped");
    }
}
