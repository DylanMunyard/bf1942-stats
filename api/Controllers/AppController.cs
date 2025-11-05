using Microsoft.AspNetCore.Mvc;
using api.Gamification.Services;
using api.Caching;
using api.ClickHouse;
using api.ClickHouse.Interfaces;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AppController(
    IBadgeDefinitionsService badgeDefinitionsService,
    IGameTrendsService gameTrendsService,
    ICacheService cacheService,
    ILogger<AppController> logger,
    IClickHouseReader clickHouseReader,
    PlayerTrackerDbContext dbContext) : ControllerBase
{

    /// <summary>
    /// Get initial data required by the UI on page load, heavily cached for performance
    /// </summary>
    [HttpGet("initialdata")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept")]
    public async Task<ActionResult<AppInitialData>> GetInitialData()
    {
        const string cacheKey = "app:initial:data:v1";

        try
        {
            // Try to get from cache first
            var cachedData = await cacheService.GetAsync<AppInitialData>(cacheKey);
            if (cachedData != null)
            {
                logger.LogDebug("Returning cached initial data");
                return Ok(cachedData);
            }

            // Generate fresh data
            var badgeDefinitions = badgeDefinitionsService.GetAllBadges();

            var initialData = new AppInitialData
            {
                BadgeDefinitions = badgeDefinitions.Select(b => new BadgeUIDefinition
                {
                    Id = b.Id,
                    Name = b.Name,
                    Description = b.UIDescription, // Use the UI-friendly description
                    Tier = b.Tier,
                    Category = b.Category,
                    Requirements = b.Requirements
                }).ToList(),
                Categories = new[]
                {
                    "performance",
                    "milestone",
                    "social",
                    "map_mastery",
                    "consistency"
                },
                Tiers = new[]
                {
                    "bronze",
                    "silver",
                    "gold",
                    "legend"
                },
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 1 hour - static data doesn't change often
            await cacheService.SetAsync(cacheKey, initialData, TimeSpan.FromHours(1));

            logger.LogInformation("Generated and cached fresh initial data with {BadgeCount} badges", badgeDefinitions.Count);

            return Ok(initialData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating initial data");
            return StatusCode(500, "An internal server error occurred while retrieving initial data.");
        }
    }

    /// <summary>
    /// Get landing page data with game trends, optimized for fast loading
    /// </summary>
    [HttpGet("landingdata")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept")]
    public async Task<ActionResult<LandingPageData>> GetLandingPageData()
    {
        const string cacheKey = "app:landing:data:v1";

        try
        {
            // Try to get from cache first
            var cachedData = await cacheService.GetAsync<LandingPageData>(cacheKey);
            if (cachedData != null)
            {
                logger.LogDebug("Returning cached landing page data");
                return Ok(cachedData);
            }

            // Generate fresh data - fetch trends and badges in parallel
            var badgeDefinitionsTask = Task.FromResult(badgeDefinitionsService.GetAllBadges());

            await Task.WhenAll(badgeDefinitionsTask);

            var landingData = new LandingPageData
            {
                BadgeDefinitions = badgeDefinitionsTask.Result.Select(b => new BadgeUIDefinition
                {
                    Id = b.Id,
                    Name = b.Name,
                    Description = b.UIDescription,
                    Tier = b.Tier,
                    Category = b.Category,
                    Requirements = b.Requirements
                }).ToList(),
                Categories = new[]
                {
                    "performance",
                    "milestone",
                    "social",
                    "map_mastery",
                    "consistency"
                },
                Tiers = new[]
                {
                    "bronze",
                    "silver",
                    "gold",
                    "legend"
                },
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 10 minutes - landing page data should be fresh but not too frequent
            await cacheService.SetAsync(cacheKey, landingData, TimeSpan.FromMinutes(10));

            logger.LogInformation("Generated and cached fresh landing page data with {BadgeCount} badges and trend data",
                badgeDefinitionsTask.Result.Count);

            return Ok(landingData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating landing page data");
            return StatusCode(500, "An internal server error occurred while retrieving landing page data.");
        }
    }

    /// <summary>
    /// Get system statistics showing data volume metrics from ClickHouse and SQLite
    /// </summary>
    [HttpGet("systemstats")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept")]
    public async Task<ActionResult<SystemStats>> GetSystemStats()
    {
        const string cacheKey = "app:system:stats:v1";

        try
        {
            // Try to get from cache first (5 minute cache)
            var cachedData = await cacheService.GetAsync<SystemStats>(cacheKey);
            if (cachedData != null)
            {
                logger.LogDebug("Returning cached system stats");
                return Ok(cachedData);
            }

            // Execute all count queries in parallel for maximum performance
            var roundsCountTask = GetClickHouseCountAsync("player_rounds", "Rounds Tracked");
            var metricsCountTask = GetClickHouseCountAsync("player_metrics", "Player Metrics Tracked");
            var serversCountTask = dbContext.Servers.CountAsync();
            var playersCountTask = dbContext.Players.CountAsync();

            await Task.WhenAll(roundsCountTask, metricsCountTask, serversCountTask, playersCountTask);

            var stats = new SystemStats
            {
                ClickHouseMetrics = new ClickHouseMetrics
                {
                    RoundsTracked = roundsCountTask.Result,
                    PlayerMetricsTracked = metricsCountTask.Result
                },
                SqliteMetrics = new SqliteMetrics
                {
                    ServersTracked = serversCountTask.Result,
                    PlayersTracked = playersCountTask.Result
                },
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 5 minutes - good balance between freshness and performance
            await cacheService.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(5));

            logger.LogInformation(
                "Generated system stats: {RoundsCount} rounds, {MetricsCount} metrics, {ServersCount} servers, {PlayersCount} players",
                stats.ClickHouseMetrics.RoundsTracked,
                stats.ClickHouseMetrics.PlayerMetricsTracked,
                stats.SqliteMetrics.ServersTracked,
                stats.SqliteMetrics.PlayersTracked);

            return Ok(stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating system stats");
            return StatusCode(500, "An internal server error occurred while retrieving system statistics.");
        }
    }

    /// <summary>
    /// Helper method to execute COUNT(*) queries against ClickHouse tables
    /// </summary>
    private async Task<long> GetClickHouseCountAsync(string tableName, string metricDescription)
    {
        try
        {
            var query = $"SELECT COUNT(*) FROM {tableName}";
            var result = await clickHouseReader.ExecuteQueryAsync(query);

            // ClickHouse returns the count as a plain number in the response
            if (long.TryParse(result.Trim(), out var count))
            {
                return count;
            }

            logger.LogWarning("Failed to parse ClickHouse count for {Table}: {Result}", tableName, result);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting count from ClickHouse table {Table}", tableName);
            return 0;
        }
    }
}

/// <summary>
/// Initial data structure optimized for UI consumption
/// </summary>
public class AppInitialData
{
    public List<BadgeUIDefinition> BadgeDefinitions { get; set; } = new();
    public string[] Categories { get; set; } = Array.Empty<string>();
    public string[] Tiers { get; set; } = Array.Empty<string>();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Landing page data structure including trends for comprehensive dashboard
/// </summary>
public class LandingPageData
{
    public List<BadgeUIDefinition> BadgeDefinitions { get; set; } = new();
    public string[] Categories { get; set; } = Array.Empty<string>();
    public string[] Tiers { get; set; } = Array.Empty<string>();
    public LandingPageTrendSummary TrendSummary { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Simplified badge definition optimized for UI rendering
/// </summary>
public class BadgeUIDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = ""; // UI-friendly description
    public string Tier { get; set; } = "";
    public string Category { get; set; } = "";
    public Dictionary<string, object> Requirements { get; set; } = new();
}

/// <summary>
/// System statistics showing the scale of data being processed across databases
/// </summary>
public class SystemStats
{
    public ClickHouseMetrics ClickHouseMetrics { get; set; } = new();
    public SqliteMetrics SqliteMetrics { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Metrics from ClickHouse analytical database
/// </summary>
public class ClickHouseMetrics
{
    /// <summary>
    /// Total number of player rounds tracked in the player_rounds table
    /// </summary>
    public long RoundsTracked { get; set; }

    /// <summary>
    /// Total number of player metrics snapshots in the player_metrics table
    /// </summary>
    public long PlayerMetricsTracked { get; set; }
}

/// <summary>
/// Metrics from SQLite operational database
/// </summary>
public class SqliteMetrics
{
    /// <summary>
    /// Total number of game servers being tracked
    /// </summary>
    public int ServersTracked { get; set; }

    /// <summary>
    /// Total number of unique players tracked
    /// </summary>
    public int PlayersTracked { get; set; }
}
