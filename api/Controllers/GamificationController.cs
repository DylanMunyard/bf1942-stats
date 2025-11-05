using Microsoft.AspNetCore.Mvc;
using api.Gamification.Models;
using api.Gamification.Services;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class GamificationController : ControllerBase
{
    private readonly GamificationService _gamificationService;
    private readonly ILogger<GamificationController> _logger;

    public GamificationController(
        GamificationService gamificationService,
        ILogger<GamificationController> logger)
    {
        _gamificationService = gamificationService;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive achievement summary for a player
    /// </summary>
    [HttpGet("player/{playerName}")]
    public async Task<ActionResult<PlayerAchievementSummary>> GetPlayerAchievements(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        try
        {
            var summary = await _gamificationService.GetPlayerAchievementSummaryAsync(playerName);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting achievements for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving player achievements.");
        }
    }

    /// <summary>
    /// Get placement summary for a player (optionally filtered by server or map)
    /// </summary>
    [HttpGet("player/{playerName}/placements")]
    public async Task<ActionResult<PlayerPlacementSummary>> GetPlayerPlacements(
        string playerName,
        [FromQuery] string? serverGuid = null,
        [FromQuery] string? mapName = null)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        try
        {
            var summary = await _gamificationService.GetPlayerPlacementSummaryAsync(playerName, serverGuid, mapName);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting placement summary for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving player placements.");
        }
    }

    /// <summary>
    /// Get recent achievements for a player
    /// </summary>
    [HttpGet("player/{playerName}/recent")]
    public async Task<ActionResult<List<Achievement>>> GetPlayerRecentAchievements(
        string playerName,
        [FromQuery] int days = 30,
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        if (days < 1 || days > 365)
            return BadRequest("Days must be between 1 and 365");

        if (limit < 1 || limit > 100)
            return BadRequest("Limit must be between 1 and 100");

        try
        {
            var summary = await _gamificationService.GetPlayerAchievementSummaryAsync(playerName);
            var recentAchievements = summary.RecentAchievements
                .Where(a => a.AchievedAt >= DateTime.UtcNow.AddDays(-days))
                .Take(limit)
                .ToList();

            return Ok(recentAchievements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent achievements for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving recent achievements.");
        }
    }

    /// <summary>
    /// Get leaderboard for specific category
    /// </summary>
    [HttpGet("leaderboard/{category}")]
    public async Task<ActionResult<GamificationLeaderboard>> GetLeaderboard(
        string category,
        [FromQuery] string period = "all_time",
        [FromQuery] int limit = 100)
    {
        var validCategories = new[] { "kill_streaks", "achievements", "milestones", "placements" };
        if (!validCategories.Contains(category.ToLower()))
            return BadRequest($"Invalid category. Valid categories: {string.Join(", ", validCategories)}");

        var validPeriods = new[] { "daily", "weekly", "monthly", "all_time" };
        if (!validPeriods.Contains(period.ToLower()))
            return BadRequest($"Invalid period. Valid periods: {string.Join(", ", validPeriods)}");

        if (limit < 1 || limit > 500)
            return BadRequest("Limit must be between 1 and 500");

        try
        {
            if (category.Equals("placements", StringComparison.OrdinalIgnoreCase))
            {
                // For placements, return a strongly-typed leaderboard tailored to placements
                var entries = await _gamificationService.GetPlacementLeaderboardAsync(limit: limit);
                return Ok(entries);
            }
            else
            {
                var leaderboard = await _gamificationService.GetLeaderboardAsync(category, period, limit);
                return Ok(leaderboard);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting leaderboard for category {Category}, period {Period}",
                category, period);
            return StatusCode(500, "An internal server error occurred while retrieving leaderboard.");
        }
    }

    /// <summary>
    /// Get all available badge definitions
    /// </summary>
    [HttpGet("badges")]
    public ActionResult<List<BadgeDefinition>> GetAllBadges()
    {
        try
        {
            var badges = _gamificationService.GetAllBadgeDefinitions();
            return Ok(badges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting badge definitions");
            return StatusCode(500, "An internal server error occurred while retrieving badge definitions.");
        }
    }

    /// <summary>
    /// Get badge definitions by category
    /// </summary>
    [HttpGet("badges/{category}")]
    public ActionResult<List<BadgeDefinition>> GetBadgesByCategory(string category)
    {
        var validCategories = new[] { "performance", "milestone", "social", "map_mastery", "consistency", "team_play" };
        if (!validCategories.Contains(category.ToLower()))
            return BadRequest($"Invalid category. Valid categories: {string.Join(", ", validCategories)}");

        try
        {
            var badges = _gamificationService.GetBadgeDefinitionsByCategory(category);
            return Ok(badges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting badge definitions for category {Category}", category);
            return StatusCode(500, "An internal server error occurred while retrieving badge definitions.");
        }
    }

    /// <summary>
    /// Check if player has specific achievement
    /// </summary>
    [HttpGet("player/{playerName}/has/{achievementId}")]
    public async Task<ActionResult<bool>> PlayerHasAchievement(string playerName, string achievementId)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        if (string.IsNullOrWhiteSpace(achievementId))
            return BadRequest("Achievement ID is required");

        try
        {
            var hasAchievement = await _gamificationService.PlayerHasAchievementAsync(playerName, achievementId);
            return Ok(hasAchievement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking achievement {AchievementId} for player {PlayerName}",
                achievementId, playerName);
            return StatusCode(500, "An internal server error occurred while checking achievement.");
        }
    }


    /// <summary>
    /// Trigger historical data processing (admin only)
    /// </summary>
    [HttpPost("admin/process-historical")]
    public Task<ActionResult> ProcessHistoricalData(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        try
        {
            // This is a long-running operation, so we'll run it in the background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _gamificationService.ProcessHistoricalDataAsync(fromDate, toDate);
                    _logger.LogInformation("Historical gamification processing completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during historical gamification processing");
                }
            });

            return Task.FromResult<ActionResult>(Accepted("Historical processing started. Check logs for progress."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting historical processing");
            return Task.FromResult<ActionResult>(StatusCode(500, "An internal server error occurred while starting historical processing."));
        }
    }

    /* 
        /// <summary>
        /// Trigger incremental processing (admin only)
        /// </summary>
        [HttpPost("admin/process-incremental")]
        public async Task<ActionResult> ProcessIncrementalData()
        {
            try
            {
                await _gamificationService.ProcessNewAchievementsAsync();
                return Ok("Incremental processing completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during incremental processing");
                return StatusCode(500, "An internal server error occurred during incremental processing.");
            }
        } */

    /// <summary>
    /// Get all achievements with pagination and filtering
    /// </summary>
    /// <remarks>
    /// Returns a paginated list of achievements with optional filtering. When a playerName is provided,
    /// the response includes a list of all achievement IDs that the player has, allowing for client-side
    /// filtering without being limited to the current page.
    /// 
    /// The PlayerAchievementIds field contains all achievement IDs the player has earned, which can be used
    /// to build achievement progress indicators, filter available achievements, or show completion status.
    /// </remarks>
    [HttpGet("achievements")]
    public async Task<ActionResult<AchievementResponse>> GetAllAchievements(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] string sortBy = "AchievedAt",
        [FromQuery] string sortOrder = "desc",
        [FromQuery] string? playerName = null,
        [FromQuery] string? achievementType = null,
        [FromQuery] string? achievementId = null,
        [FromQuery] string? tier = null,
        [FromQuery] DateTime? achievedFrom = null,
        [FromQuery] DateTime? achievedTo = null,
        [FromQuery] string? serverGuid = null,
        [FromQuery] string? mapName = null)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        // Valid sort fields
        var validSortFields = new[]
        {
            "PlayerName", "AchievementType", "AchievementId", "AchievementName",
            "Tier", "Value", "AchievedAt", "ProcessedAt", "ServerGuid", "MapName"
        };

        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        if (!new[] { "asc", "desc" }.Contains(sortOrder.ToLower()))
            return BadRequest("Sort order must be 'asc' or 'desc'");

        // Validate date range
        if (achievedFrom.HasValue && achievedTo.HasValue && achievedFrom > achievedTo)
            return BadRequest("AchievedFrom cannot be greater than AchievedTo");

        try
        {
            var result = await _gamificationService.GetAllAchievementsWithPlayerIdsAsync(
                page, pageSize, sortBy, sortOrder, playerName, achievementType,
                achievementId, tier, achievedFrom, achievedTo, serverGuid, mapName);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting achievements with paging");
            return StatusCode(500, "An internal server error occurred while retrieving achievements.");
        }
    }

    /// <summary>
    /// Get system statistics
    /// </summary>
    [HttpGet("stats")]
    public Task<ActionResult> GetGamificationStats()
    {
        try
        {
            // This would return overall system stats like total achievements, top players, etc.
            // For now, return a placeholder
            var stats = new
            {
                Message = "Gamification system is active",
                AvailableBadges = _gamificationService.GetAllBadgeDefinitions().Count,
                Categories = new[] { "performance", "milestone", "social", "map_mastery", "consistency" },
                LastUpdated = DateTime.UtcNow
            };

            return Task.FromResult<ActionResult>(Ok(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting gamification stats");
            return Task.FromResult<ActionResult>(StatusCode(500, "An internal server error occurred while retrieving stats."));
        }
    }
}
