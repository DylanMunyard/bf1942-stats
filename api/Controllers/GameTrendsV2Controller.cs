using Microsoft.AspNetCore.Mvc;
using api.Analytics.Models;
using api.Caching;
using api.GameTrends;
using api.GameTrends.Models;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/v2/game-trends")]
public class GameTrendsV2Controller(
    ISqliteGameTrendsService sqliteGameTrendsService,
    ICacheService cacheService,
    ILogger<GameTrendsV2Controller> logger) : ControllerBase
{
    /// <summary>
    /// Gets Google-style busy indicator comparing current activity to historical patterns, grouped by server.
    /// Uses SQLite aggregates for v2 endpoints.
    /// </summary>
    /// <param name="serverGuids">Required array of server GUIDs to analyze</param>
    [HttpGet("busy-indicator")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)] // 5 minutes cache
    public async Task<ActionResult<GroupedServerBusyIndicatorResult>> GetBusyIndicator(
        [FromQuery] string[] serverGuids)
    {
        if (serverGuids == null || serverGuids.Length == 0)
        {
            return BadRequest("Server GUIDs are required");
        }

        try
        {
            var serverGuidsKey = string.Join(",", serverGuids.OrderBy(x => x));
            var cacheKey = $"trends:v2:busy:servers:{serverGuidsKey}";
            var cachedData = await cacheService.GetAsync<GroupedServerBusyIndicatorResult>(cacheKey);

            if (cachedData != null)
            {
                logger.LogDebug("Returning cached v2 server busy indicator for {ServerCount} servers",
                    serverGuids.Length);
                return Ok(cachedData);
            }

            var busyIndicator = await sqliteGameTrendsService.GetServerBusyIndicatorAsync(serverGuids);

            // Cache for 5 minutes - busy indicator should be current
            await cacheService.SetAsync(cacheKey, busyIndicator, TimeSpan.FromMinutes(5));

            logger.LogDebug("Generated v2 server busy indicator for {ServerCount} servers",
                serverGuids.Length);

            return Ok(busyIndicator);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating v2 server busy indicator for {ServerCount} servers",
                serverGuids?.Length ?? 0);
            return StatusCode(500, "Failed to generate server busy indicator");
        }
    }

    /// <summary>
    /// Gets comprehensive trend summary optimized for landing page display.
    /// Uses SQLite aggregates for v2 endpoints.
    /// </summary>
    /// <param name="game">Optional filter by game (bf1942, fh2, bfv)</param>
    [HttpGet("landing-summary")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)] // 10 minutes cache
    public async Task<ActionResult<LandingPageTrendSummary>> GetLandingPageTrendSummary(
        [FromQuery] string? game = null)
    {
        try
        {
            var cacheKey = $"trends:v2:landing:{game ?? "all"}";
            var cachedData = await cacheService.GetAsync<LandingPageTrendSummary>(cacheKey);

            if (cachedData != null)
            {
                logger.LogDebug("Returning cached v2 landing page trend summary for game {GameId}", game ?? "all");
                return Ok(cachedData);
            }

            var insights = await sqliteGameTrendsService.GetSmartPredictionInsightsAsync(game);

            var summary = new LandingPageTrendSummary
            {
                Insights = insights,
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 10 minutes - landing page data should be fresh but not too frequent
            await cacheService.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(10));

            logger.LogDebug("Generated v2 landing page trend summary for game {GameId}", game ?? "all");

            return Ok(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating v2 landing page trend summary for game {GameId}", game);
            return StatusCode(500, "Failed to generate landing page trend summary");
        }
    }
}
