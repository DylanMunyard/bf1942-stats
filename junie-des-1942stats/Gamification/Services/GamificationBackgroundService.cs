using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class GamificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<GamificationBackgroundService> _logger;

    public GamificationBackgroundService(IServiceProvider services, ILogger<GamificationBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gamification background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var gamificationService = scope.ServiceProvider.GetRequiredService<GamificationService>();

                // Process new achievements every 5 minutes
                // await gamificationService.ProcessNewAchievementsAsync();
                
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