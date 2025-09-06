using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Gamification.Services;
using junie_des_1942stats.Caching;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AppController : ControllerBase
{
    private readonly BadgeDefinitionsService _badgeDefinitionsService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<AppController> _logger;

    public AppController(
        BadgeDefinitionsService badgeDefinitionsService,
        ICacheService cacheService,
        ILogger<AppController> logger)
    {
        _badgeDefinitionsService = badgeDefinitionsService;
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