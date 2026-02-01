using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using api.Gamification.Services;
using api.Caching;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AppController(
    IBadgeDefinitionsService badgeDefinitionsService,
    ICacheService cacheService,
    ILogger<AppController> logger,
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

            SiteNoticeDto? siteNotice = null;
            var siteNoticeRow = await dbContext.AppData
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == "site_notice");
            if (siteNoticeRow != null && !string.IsNullOrEmpty(siteNoticeRow.Value))
            {
                try
                {
                    siteNotice = JsonSerializer.Deserialize<SiteNoticeDto>(siteNoticeRow.Value);
                }
                catch (JsonException)
                {
                    // Invalid JSON stored for site_notice — leave null
                }
            }

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
                GeneratedAt = DateTime.UtcNow,
                SiteNotice = siteNotice
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
    /// Get system statistics showing data volume metrics
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
            var serversCountTask = dbContext.Servers.CountAsync();
            var playersCountTask = dbContext.Players.CountAsync();

            await Task.WhenAll(serversCountTask, playersCountTask);

            var stats = new SystemStats
            {
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
                "Generated system stats: {ServersCount} servers, {PlayersCount} players",
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
    /// <summary>Optional site-wide notice banner (from app_data.site_notice).</summary>
    public SiteNoticeDto? SiteNotice { get; set; }
}

/// <summary>
/// Site notice banner payload (stored as JSON in app_data.site_notice).
/// </summary>
public class SiteNoticeDto
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Type { get; set; } = "info"; // info | warning | success | error
    public bool Dismissible { get; set; } = true;
    public string? ExpiresAt { get; set; }
    public string CreatedAt { get; set; } = "";
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
    public SqliteMetrics SqliteMetrics { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
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
