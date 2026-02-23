using api.PlayerRelationships.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

/// <summary>
/// Background service that periodically runs community detection algorithms on the player network.
/// </summary>
public class CommunityDetectionService(
    IServiceProvider serviceProvider,
    ILogger<CommunityDetectionService> logger) : BackgroundService
{
    private readonly TimeSpan _delay = TimeSpan.FromHours(24); // Run daily

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Community Detection Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_delay, stoppingToken);
                
                using var scope = serviceProvider.CreateScope();
                var relationshipService = scope.ServiceProvider.GetRequiredService<IPlayerRelationshipService>();
                
                logger.LogInformation("Running community detection");
                var result = await relationshipService.DetectAndStoreCommunities(stoppingToken);
                logger.LogInformation("Community detection completed: {Result}", result);
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in community detection service");
            }
        }

        logger.LogInformation("Community Detection Service stopped");
    }
}