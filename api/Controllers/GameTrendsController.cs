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
}
