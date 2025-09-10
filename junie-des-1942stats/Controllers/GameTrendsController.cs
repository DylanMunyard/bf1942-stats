using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Caching;
using Microsoft.Extensions.Logging;
using System.Linq;

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
    /// Gets current activity status across all games and servers.
    /// Shows real-time activity to answer "is it busy right now?"
    /// </summary>
    [HttpGet("current-activity")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)] // 1 minute cache
    public async Task<ActionResult<List<CurrentActivityStatus>>> GetCurrentActivityStatus(
        [FromQuery] string? game = null)
    {
        try
        {
            var cacheKey = $"trends:current:activity:{game ?? "all"}";
            var cachedData = await _cacheService.GetAsync<List<CurrentActivityStatus>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached current activity status for game {Game}", game ?? "all");
                return Ok(cachedData);
            }

            var currentActivity = await _gameTrendsService.GetCurrentActivityStatusAsync(game);
            
            // Cache for 5 minutes - current activity needs to be relatively fresh
            await _cacheService.SetAsync(cacheKey, currentActivity, TimeSpan.FromMinutes(1));
            
            _logger.LogInformation("Retrieved current activity status for {ServerCount} servers, game {Game}", 
                currentActivity.Count, game ?? "all");

            return Ok(currentActivity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current activity status for game {Game}", game);
            return StatusCode(500, "Failed to retrieve current activity status");
        }
    }

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
            var cachedData = await _cacheService.GetAsync<List<WeeklyActivityPattern>>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached weekly activity patterns for game {GameId}", game ?? "all");
                return Ok(cachedData);
            }

            var patterns = await _gameTrendsService.GetWeeklyActivityPatternsAsync(game, daysPeriod);
            
            // Cache for 1 hour - weekly patterns are stable
            await _cacheService.SetAsync(cacheKey, patterns, TimeSpan.FromHours(1));
            
            _logger.LogInformation("Retrieved {PatternCount} weekly activity patterns for game {GameId}", 
                patterns.Count, game ?? "all");

            return Ok(patterns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weekly activity patterns for game {GameId}", game);
            return StatusCode(500, "Failed to retrieve weekly activity patterns");
        }
    }

    /// <summary>
    /// Gets personalized trend insights to help players decide when to play.
    /// Answers "is it busy now?" and "will it get busier?" based on player's timezone.
    /// </summary>
    /// <param name="game">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="timeZoneOffsetHours">Player's timezone offset from UTC (e.g., +14 for Australia/Sydney)</param>
    [HttpGet("insights")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)] // 15 minutes cache
    public async Task<ActionResult<SmartPredictionInsights>> GetTrendInsights(
        [FromQuery] string? game = null, 
        [FromQuery] int timeZoneOffsetHours = 0)
    {
        try
        {
            var cacheKey = $"trends:insights:{game ?? "all"}:{timeZoneOffsetHours}";
            var cachedData = await _cacheService.GetAsync<SmartPredictionInsights>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached trend insights for game {GameId} with timezone offset {Offset}", 
                    game ?? "all", timeZoneOffsetHours);
                return Ok(cachedData);
            }

            var insights = await _gameTrendsService.GetSmartPredictionInsightsAsync(game, timeZoneOffsetHours);
            
            // Cache for 15 minutes - insights should be relatively current
            await _cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(15));
            
            _logger.LogInformation("Generated trend insights for game {GameId} with timezone offset {Offset}", 
                game ?? "all", timeZoneOffsetHours);

            return Ok(insights);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating trend insights for game {GameId}", game);
            return StatusCode(500, "Failed to generate trend insights");
        }
    }

    /// <summary>
    /// Gets Google-style busy indicator comparing current activity to historical patterns, grouped by server.
    /// Shows "Busier than usual", "Busy", "As busy as usual", etc. for each specified server.
    /// </summary>
    /// <param name="serverGuids">Required array of server GUIDs to analyze</param>
    /// <param name="timeZoneOffsetHours">Player's timezone offset from UTC</param>
    [HttpGet("busy-indicator")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)] // 5 minutes cache
    public async Task<ActionResult<GroupedServerBusyIndicatorResult>> GetBusyIndicator(
    [FromQuery] string[] serverGuids,
    [FromQuery] int timeZoneOffsetHours = 0)
{
    if (serverGuids == null || serverGuids.Length == 0)
    {
        return BadRequest("Server GUIDs are required");
    }

    try
    {
        var serverGuidsKey = string.Join(",", serverGuids.OrderBy(x => x));
        var cacheKey = $"trends:busy:servers:{serverGuidsKey}:{timeZoneOffsetHours}";
        var cachedData = await _cacheService.GetAsync<GroupedServerBusyIndicatorResult>(cacheKey);
        
        if (cachedData != null)
        {
            _logger.LogDebug("Returning cached server busy indicator for {ServerCount} servers", 
                serverGuids.Length);
            return Ok(cachedData);
        }

        var busyIndicator = await _gameTrendsService.GetServerBusyIndicatorAsync(serverGuids, timeZoneOffsetHours);
        
        // Cache for 5 minutes - busy indicator should be current
        await _cacheService.SetAsync(cacheKey, busyIndicator, TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("Generated server busy indicator for {ServerCount} servers", 
            serverGuids.Length);

        return Ok(busyIndicator);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error generating server busy indicator for {ServerCount} servers", 
            serverGuids?.Length ?? 0);
        return StatusCode(500, "Failed to generate server busy indicator");
    }
}

    /// <summary>
    /// Gets comprehensive trend summary optimized for landing page display.
    /// Combines multiple trend data points into a single fast-loading response.
    /// </summary>
    /// <param name="game">Optional filter by game (bf1942, fh2, bfv)</param>
    /// <param name="timeZoneOffsetHours">Player's timezone offset from UTC</param>
    [HttpGet("landing-summary")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)] // 10 minutes cache
    public async Task<ActionResult<LandingPageTrendSummary>> GetLandingPageTrendSummary(
        [FromQuery] string? game = null, 
        [FromQuery] int timeZoneOffsetHours = 0)
    {
        try
        {
            var cacheKey = $"trends:landing:{game ?? "all"}:{timeZoneOffsetHours}";
            var cachedData = await _cacheService.GetAsync<LandingPageTrendSummary>(cacheKey);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached landing page trend summary for game {GameId}", game ?? "all");
                return Ok(cachedData);
            }

            // Fetch multiple trend data points in parallel for fast response
            var currentActivityTask = _gameTrendsService.GetCurrentActivityStatusAsync();
            var trendsInsightsTask = _gameTrendsService.GetSmartPredictionInsightsAsync(game, timeZoneOffsetHours);

            await Task.WhenAll(currentActivityTask, trendsInsightsTask);

            var summary = new LandingPageTrendSummary
            {
                CurrentActivity = currentActivityTask.Result.Take(5).ToList(), // Top 5 active servers
                Insights = trendsInsightsTask.Result,
                GeneratedAt = DateTime.UtcNow
            };
            
            // Cache for 10 minutes - landing page data should be fresh but not too frequent
            await _cacheService.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(10));
            
            _logger.LogInformation("Generated landing page trend summary for game {GameId}", game ?? "all");

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating landing page trend summary for game {GameId}", game);
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
    public SmartPredictionInsights Insights { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}