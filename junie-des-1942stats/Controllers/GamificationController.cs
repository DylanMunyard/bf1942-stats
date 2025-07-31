using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.Gamification.Services;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

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
    /// Get achievements by type for a player
    /// </summary>
    [HttpGet("player/{playerName}/{achievementType}")]
    public async Task<ActionResult<List<Achievement>>> GetPlayerAchievementsByType(
        string playerName,
        string achievementType)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        var validTypes = new[] { "kill_streak", "badge", "milestone", "ranking" };
        if (!validTypes.Contains(achievementType.ToLower()))
            return BadRequest($"Invalid achievement type. Valid types: {string.Join(", ", validTypes)}");

        try
        {
            var summary = await _gamificationService.GetPlayerAchievementSummaryAsync(playerName);
            
            var achievements = achievementType.ToLower() switch
            {
                "kill_streak" => summary.RecentAchievements.Where(a => a.AchievementType == AchievementTypes.KillStreak).ToList(),
                "badge" => summary.AllBadges,
                "milestone" => summary.Milestones,
                _ => new List<Achievement>()
            };

            return Ok(achievements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {AchievementType} achievements for player {PlayerName}", 
                achievementType, playerName);
            return StatusCode(500, "An internal server error occurred while retrieving achievements.");
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
        var validCategories = new[] { "kill_streaks", "achievements", "milestones" };
        if (!validCategories.Contains(category.ToLower()))
            return BadRequest($"Invalid category. Valid categories: {string.Join(", ", validCategories)}");

        var validPeriods = new[] { "daily", "weekly", "monthly", "all_time" };
        if (!validPeriods.Contains(period.ToLower()))
            return BadRequest($"Invalid period. Valid periods: {string.Join(", ", validPeriods)}");

        if (limit < 1 || limit > 500)
            return BadRequest("Limit must be between 1 and 500");

        try
        {
            var leaderboard = await _gamificationService.GetLeaderboardAsync(category, period, limit);
            return Ok(leaderboard);
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
        var validCategories = new[] { "performance", "milestone", "social", "map_mastery", "consistency" };
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