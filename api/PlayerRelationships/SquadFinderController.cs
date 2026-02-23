using api.PlayerRelationships.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

[ApiController]
[Route("stats/squad-finder")]
public class SquadFinderController(
    IPlayerRelationshipService relationshipService,
    ILogger<SquadFinderController> logger) : ControllerBase
{
    /// <summary>
    /// Get squad recommendations for a player.
    /// </summary>
    [HttpGet("players/{playerName}/recommendations")]
    public async Task<ActionResult<List<SquadRecommendation>>> GetRecommendations(
        string playerName,
        [FromQuery] int limit = 10,
        [FromQuery] bool onlineOnly = false)
    {
        try
        {
            var recommendations = await relationshipService.GetSquadRecommendationsAsync(
                playerName, limit, onlineOnly);
            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting squad recommendations for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while finding squad recommendations");
        }
    }

    /// <summary>
    /// Submit feedback on a squad recommendation.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> SubmitFeedback([FromBody] SquadFeedbackRequest request)
    {
        try
        {
            await relationshipService.RecordSquadRecommendationFeedback(
                request.PlayerName,
                request.RecommendedPlayer,
                request.WasHelpful);
            
            return Ok(new { message = "Feedback recorded successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording squad feedback");
            return StatusCode(500, "An error occurred while recording feedback");
        }
    }
}

/// <summary>
/// Request model for squad recommendation feedback.
/// </summary>
public record SquadFeedbackRequest
{
    public required string PlayerName { get; init; }
    public required string RecommendedPlayer { get; init; }
    public required bool WasHelpful { get; init; }
}