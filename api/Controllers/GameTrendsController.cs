using Microsoft.AspNetCore.Mvc;
using api.Analytics.Models;
using api.Caching;
using api.GameTrends;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class GameTrendsController(
    ISqliteGameTrendsService sqliteGameTrendsService,
    ICacheService cacheService,
    ILogger<GameTrendsController> logger) : ControllerBase
{
    /// <summary>
    /// Gets weekly activity patterns showing weekend vs weekday differences.
    /// Helps identify when servers are most active throughout the week.
    /// </summary>
    /// <param name="game">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="daysPeriod">Number of days to analyze (default: 30)</param>
    [HttpGet("weekly-patterns")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)] // 1 hour cache
    public async Task<ActionResult<List<WeeklyActivityPattern>>> GetWeeklyActivityPatterns(
        [FromQuery] string? game = null,
        [FromQuery] int daysPeriod = 30)
    {
        try
        {
            var cacheKey = $"trends:weekly:{game ?? "all"}:{daysPeriod}";
            var cachedData = await cacheService.GetAsync<List<WeeklyActivityPattern>>(cacheKey);

            if (cachedData != null)
            {
                logger.LogDebug("Returning cached weekly activity patterns for game {GameId}", game ?? "all");
                return Ok(cachedData);
            }

            var patterns = await sqliteGameTrendsService.GetWeeklyActivityPatternsAsync(game, daysPeriod);

            // Cache for 1 hour - weekly patterns are stable
            await cacheService.SetAsync(cacheKey, patterns, TimeSpan.FromHours(1));

            logger.LogInformation("Retrieved {PatternCount} weekly activity patterns for game {GameId}",
                patterns.Count, game ?? "all");

            return Ok(patterns);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving weekly activity patterns for game {GameId}", game);
            return StatusCode(500, "Failed to retrieve weekly activity patterns");
        }
    }

    /// <summary>
    /// Gets Google-style busy indicator comparing current activity to historical patterns, grouped by server.
    /// Shows "Busier than usual", "Busy", "As busy as usual", etc. for each specified server.
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
            var cacheKey = $"trends:busy:servers:{serverGuidsKey}";
            var cachedData = await cacheService.GetAsync<GroupedServerBusyIndicatorResult>(cacheKey);

            if (cachedData != null)
            {
                logger.LogDebug("Returning cached server busy indicator for {ServerCount} servers",
                    serverGuids.Length);
                return Ok(cachedData);
            }

            var busyIndicator = await sqliteGameTrendsService.GetServerBusyIndicatorAsync(serverGuids);

            // Cache for 5 minutes - busy indicator should be current
            await cacheService.SetAsync(cacheKey, busyIndicator, TimeSpan.FromMinutes(5));

            logger.LogInformation("Generated server busy indicator for {ServerCount} servers",
                serverGuids.Length);

            return Ok(busyIndicator);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating server busy indicator for {ServerCount} servers",
                serverGuids?.Length ?? 0);
            return StatusCode(500, "Failed to generate server busy indicator");
        }
    }

    /// <summary>
    /// Gets comprehensive trend summary optimized for landing page display.
    /// Combines multiple trend data points into a single fast-loading response.
    /// </summary>
    /// <param name="game">Optional filter by game (bf1942, fh2, bfv)</param>
    [HttpGet("landing-summary")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)] // 10 minutes cache
    public async Task<ActionResult<LandingPageTrendSummary>> GetLandingPageTrendSummary(
    [FromQuery] string? game = null)
    {
        try
        {
            var cacheKey = $"trends:landing:{game ?? "all"}";
            var cachedData = await cacheService.GetAsync<LandingPageTrendSummary>(cacheKey);

            if (cachedData != null)
            {
                logger.LogDebug("Returning cached landing page trend summary for game {GameId}", game ?? "all");
                return Ok(cachedData);
            }

            // Get insights which now includes current player count and comparison
            var insights = await sqliteGameTrendsService.GetSmartPredictionInsightsAsync(game);

            var summary = new LandingPageTrendSummary
            {
                Insights = insights,
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 10 minutes - landing page data should be fresh but not too frequent
            await cacheService.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(10));

            logger.LogInformation("Generated landing page trend summary for game {GameId}", game ?? "all");

            return Ok(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating landing page trend summary for game {GameId}", game);
            return StatusCode(500, "Failed to generate landing page trend summary");
        }
    }
}

/// <summary>
/// Optimized summary for landing page display
/// </summary>
public class LandingPageTrendSummary
{
    public SmartPredictionInsights Insights { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
