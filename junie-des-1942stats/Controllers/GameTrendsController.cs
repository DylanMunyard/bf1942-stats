using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Caching;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class GameTrendsController : ControllerBase
{
    private readonly GameTrendsService _gameTrendsService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<GameTrendsController> _logger;

    public GameTrendsController(
        GameTrendsService gameTrendsService,
        ICacheService cacheService,
        ILogger<GameTrendsController> logger)
    {
        _gameTrendsService = gameTrendsService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Gets hourly activity trends for game activity analysis.
    /// Perfect for understanding peak gaming hours and planning when to play.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="daysPeriod">Number of days to analyze (default: 30)</param>
    [HttpGet("hourly-activity")]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any)] // 30 minutes cache
    public async Task<ActionResult<List<HourlyActivityTrend>>> GetHourlyActivityTrends(
        [FromQuery] string? gameId = null, 
        [FromQuery] int daysPeriod = 30)
    {
        try
        {
            var cacheKey = $"trends:hourly:{gameId ?? "all"}:{daysPeriod}";
            var cachedData = await _cacheService.GetAsync<List<HourlyActivityTrend>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached hourly activity trends for game {GameId}", gameId ?? "all");
                return Ok(cachedData);
            }

            var trends = await _gameTrendsService.GetHourlyActivityTrendsAsync(gameId, daysPeriod);
            
            // Cache for 30 minutes - trend data doesn't change frequently
            await _cacheService.SetAsync(cacheKey, trends, TimeSpan.FromMinutes(30));
            
            _logger.LogInformation("Retrieved {TrendCount} hourly activity trends for game {GameId}", 
                trends.Count, gameId ?? "all");

            return Ok(trends);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hourly activity trends for game {GameId}", gameId);
            return StatusCode(500, "Failed to retrieve hourly activity trends");
        }
    }

    /// <summary>
    /// Gets server-specific activity trends to identify which servers are busiest at different times.
    /// Helps players find active servers during their preferred gaming hours.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="daysPeriod">Number of days to analyze (default: 7)</param>
    [HttpGet("server-activity")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)] // 15 minutes cache
    public async Task<ActionResult<List<ServerActivityTrend>>> GetServerActivityTrends(
        [FromQuery] string? gameId = null, 
        [FromQuery] int daysPeriod = 7)
    {
        try
        {
            var cacheKey = $"trends:server:{gameId ?? "all"}:{daysPeriod}";
            var cachedData = await _cacheService.GetAsync<List<ServerActivityTrend>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached server activity trends for game {GameId}", gameId ?? "all");
                return Ok(cachedData);
            }

            var trends = await _gameTrendsService.GetServerActivityTrendsAsync(gameId, daysPeriod);
            
            // Cache for 15 minutes - server activity changes more frequently
            await _cacheService.SetAsync(cacheKey, trends, TimeSpan.FromMinutes(15));
            
            _logger.LogInformation("Retrieved {TrendCount} server activity trends for game {GameId}", 
                trends.Count, gameId ?? "all");

            return Ok(trends);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving server activity trends for game {GameId}", gameId);
            return StatusCode(500, "Failed to retrieve server activity trends");
        }
    }

    /// <summary>
    /// Gets current activity status across all games and servers.
    /// Shows real-time activity to answer "is it busy right now?"
    /// </summary>
    [HttpGet("current-activity")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)] // 5 minutes cache
    public async Task<ActionResult<List<CurrentActivityStatus>>> GetCurrentActivityStatus()
    {
        try
        {
            const string cacheKey = "trends:current:activity";
            var cachedData = await _cacheService.GetAsync<List<CurrentActivityStatus>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached current activity status");
                return Ok(cachedData);
            }

            var currentActivity = await _gameTrendsService.GetCurrentActivityStatusAsync();
            
            // Cache for 5 minutes - current activity needs to be relatively fresh
            await _cacheService.SetAsync(cacheKey, currentActivity, TimeSpan.FromMinutes(5));
            
            _logger.LogInformation("Retrieved current activity status for {ServerCount} servers", 
                currentActivity.Count);

            return Ok(currentActivity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current activity status");
            return StatusCode(500, "Failed to retrieve current activity status");
        }
    }

    /// <summary>
    /// Gets weekly activity patterns showing weekend vs weekday differences.
    /// Helps identify when servers are most active throughout the week.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="daysPeriod">Number of days to analyze (default: 30)</param>
    [HttpGet("weekly-patterns")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)] // 1 hour cache
    public async Task<ActionResult<List<WeeklyActivityPattern>>> GetWeeklyActivityPatterns(
        [FromQuery] string? gameId = null, 
        [FromQuery] int daysPeriod = 30)
    {
        try
        {
            var cacheKey = $"trends:weekly:{gameId ?? "all"}:{daysPeriod}";
            var cachedData = await _cacheService.GetAsync<List<WeeklyActivityPattern>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached weekly activity patterns for game {GameId}", gameId ?? "all");
                return Ok(cachedData);
            }

            var patterns = await _gameTrendsService.GetWeeklyActivityPatternsAsync(gameId, daysPeriod);
            
            // Cache for 1 hour - weekly patterns are stable
            await _cacheService.SetAsync(cacheKey, patterns, TimeSpan.FromHours(1));
            
            _logger.LogInformation("Retrieved {PatternCount} weekly activity patterns for game {GameId}", 
                patterns.Count, gameId ?? "all");

            return Ok(patterns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weekly activity patterns for game {GameId}", gameId);
            return StatusCode(500, "Failed to retrieve weekly activity patterns");
        }
    }

    /// <summary>
    /// Gets game mode activity trends including special events like CTF nights.
    /// Identifies when specific maps/modes are most popular.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="daysPeriod">Number of days to analyze (default: 30)</param>
    [HttpGet("gamemode-activity")]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any)] // 30 minutes cache
    public async Task<ActionResult<List<GameModeActivityTrend>>> GetGameModeActivityTrends(
        [FromQuery] string? gameId = null, 
        [FromQuery] int daysPeriod = 30)
    {
        try
        {
            var cacheKey = $"trends:gamemode:{gameId ?? "all"}:{daysPeriod}";
            var cachedData = await _cacheService.GetAsync<List<GameModeActivityTrend>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached game mode activity trends for game {GameId}", gameId ?? "all");
                return Ok(cachedData);
            }

            var trends = await _gameTrendsService.GetGameModeActivityTrendsAsync(gameId, daysPeriod);
            
            // Cache for 30 minutes - game mode trends change moderately
            await _cacheService.SetAsync(cacheKey, trends, TimeSpan.FromMinutes(30));
            
            _logger.LogInformation("Retrieved {TrendCount} game mode activity trends for game {GameId}", 
                trends.Count, gameId ?? "all");

            return Ok(trends);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving game mode activity trends for game {GameId}", gameId);
            return StatusCode(500, "Failed to retrieve game mode activity trends");
        }
    }

    /// <summary>
    /// Gets personalized trend insights to help players decide when to play.
    /// Answers "is it busy now?" and "will it get busier?" based on player's timezone.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="timeZoneOffsetHours">Player's timezone offset from UTC (e.g., +14 for Australia/Sydney)</param>
    [HttpGet("insights")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)] // 15 minutes cache
    public async Task<ActionResult<TrendInsights>> GetTrendInsights(
        [FromQuery] string? gameId = null, 
        [FromQuery] int timeZoneOffsetHours = 0)
    {
        try
        {
            var cacheKey = $"trends:insights:{gameId ?? "all"}:{timeZoneOffsetHours}";
            var cachedData = await _cacheService.GetAsync<TrendInsights>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached trend insights for game {GameId} with timezone offset {Offset}", 
                    gameId ?? "all", timeZoneOffsetHours);
                return Ok(cachedData);
            }

            var insights = await _gameTrendsService.GetTrendInsightsAsync(gameId, timeZoneOffsetHours);
            
            // Cache for 15 minutes - insights should be relatively current
            await _cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(15));
            
            _logger.LogInformation("Generated trend insights for game {GameId} with timezone offset {Offset}", 
                gameId ?? "all", timeZoneOffsetHours);

            return Ok(insights);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating trend insights for game {GameId}", gameId);
            return StatusCode(500, "Failed to generate trend insights");
        }
    }

    /// <summary>
    /// Gets comprehensive trend summary optimized for landing page display.
    /// Combines multiple trend data points into a single fast-loading response.
    /// </summary>
    /// <param name="gameId">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="timeZoneOffsetHours">Player's timezone offset from UTC</param>
    [HttpGet("landing-summary")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)] // 10 minutes cache
    public async Task<ActionResult<LandingPageTrendSummary>> GetLandingPageTrendSummary(
        [FromQuery] string? gameId = null, 
        [FromQuery] int timeZoneOffsetHours = 0)
    {
        try
        {
            var cacheKey = $"trends:landing:{gameId ?? "all"}:{timeZoneOffsetHours}";
            var cachedData = await _cacheService.GetAsync<LandingPageTrendSummary>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached landing page trend summary for game {GameId}", gameId ?? "all");
                return Ok(cachedData);
            }

            // Fetch multiple trend data points in parallel for fast response
            var currentActivityTask = _gameTrendsService.GetCurrentActivityStatusAsync();
            var trendsInsightsTask = _gameTrendsService.GetTrendInsightsAsync(gameId, timeZoneOffsetHours);
            var hourlyTrendsTask = _gameTrendsService.GetHourlyActivityTrendsAsync(gameId, 7); // Last week only for landing page

            await Task.WhenAll(currentActivityTask, trendsInsightsTask, hourlyTrendsTask);

            var summary = new LandingPageTrendSummary
            {
                CurrentActivity = currentActivityTask.Result.Take(5).ToList(), // Top 5 active servers
                Insights = trendsInsightsTask.Result,
                HourlyTrends = hourlyTrendsTask.Result.Take(24).ToList(), // Last 24 hours pattern
                GeneratedAt = DateTime.UtcNow
            };
            
            // Cache for 10 minutes - landing page data should be fresh but not too frequent
            await _cacheService.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(10));
            
            _logger.LogInformation("Generated landing page trend summary for game {GameId}", gameId ?? "all");

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating landing page trend summary for game {GameId}", gameId);
            return StatusCode(500, "Failed to generate landing page trend summary");
        }
    }
}

/// <summary>
/// Optimized summary for landing page display
/// </summary>
public class LandingPageTrendSummary
{
    public List<CurrentActivityStatus> CurrentActivity { get; set; } = new();
    public TrendInsights Insights { get; set; } = new();
    public List<HourlyActivityTrend> HourlyTrends { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}