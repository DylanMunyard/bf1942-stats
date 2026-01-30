using Microsoft.AspNetCore.Mvc;
using api.Gamification.Models;
using api.Gamification.Services;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class GamificationController(
    GamificationService gamificationService,
    ILogger<GamificationController> logger) : ControllerBase
{
    /// <summary>
    /// Get hero achievements for a player (latest milestone + 5 recent achievements with full details)
    /// </summary>
    [HttpGet("player/{playerName}/hero-achievements")]
    public async Task<ActionResult<List<Achievement>>> GetPlayerHeroAchievements(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        try
        {
            var heroAchievements = await gamificationService.GetPlayerHeroAchievementsAsync(playerName);
            return Ok(heroAchievements);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting hero achievements for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving player hero achievements.");
        }
    }

    /// <summary>
    /// Get grouped achievement counts for a player
    /// </summary>
    [HttpGet("player/{playerName}/achievement-groups")]
    public async Task<ActionResult<List<PlayerAchievementGroup>>> GetPlayerAchievementGroups(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name is required");

        try
        {
            var groups = await gamificationService.GetPlayerAchievementGroupsAsync(playerName);
            return Ok(groups);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting grouped achievements for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving player achievements.");
        }
    }

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
            var result = await gamificationService.GetAllAchievementsWithPlayerIdsAsync(
                page, pageSize, sortBy, sortOrder, playerName, achievementType,
                achievementId, tier, achievedFrom, achievedTo, serverGuid, mapName);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting achievements with paging");
            return StatusCode(500, "An internal server error occurred while retrieving achievements.");
        }
    }
}
