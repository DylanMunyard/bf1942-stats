using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Gamification.Services;
using junie_des_1942stats.Caching;
using junie_des_1942stats.ClickHouse;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AppController : ControllerBase
{
    private readonly BadgeDefinitionsService _badgeDefinitionsService;
    private readonly GameTrendsService _gameTrendsService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<AppController> _logger;

    public AppController(
        BadgeDefinitionsService badgeDefinitionsService,
        GameTrendsService gameTrendsService,
        ICacheService cacheService,
        ILogger<AppController> logger)
    {
        _badgeDefinitionsService = badgeDefinitionsService;
        _gameTrendsService = gameTrendsService;
        _cacheService = cacheService;
        _logger = logger;
    }

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
            var cachedData = await _cacheService.GetAsync<AppInitialData>(cacheKey);
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached initial data");
                return Ok(cachedData);
            }

            // Generate fresh data
            var badgeDefinitions = _badgeDefinitionsService.GetAllBadges();

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
            await _cacheService.SetAsync(cacheKey, initialData, TimeSpan.FromHours(1));

            _logger.LogInformation("Generated and cached fresh initial data with {BadgeCount} badges", badgeDefinitions.Count);

            return Ok(initialData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating initial data");
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
            var cachedData = await _cacheService.GetAsync<LandingPageData>(cacheKey);
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached landing page data");
                return Ok(cachedData);
            }

            // Generate fresh data - fetch trends and badges in parallel
            var badgeDefinitionsTask = Task.FromResult(_badgeDefinitionsService.GetAllBadges());

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
            await _cacheService.SetAsync(cacheKey, landingData, TimeSpan.FromMinutes(10));

            _logger.LogInformation("Generated and cached fresh landing page data with {BadgeCount} badges and trend data",
                badgeDefinitionsTask.Result.Count);

            return Ok(landingData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating landing page data");
            return StatusCode(500, "An internal server error occurred while retrieving landing page data.");
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