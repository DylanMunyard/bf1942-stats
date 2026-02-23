using api.PlayerRelationships.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

[ApiController]
[Route("stats/communities")]
public class CommunitiesController(
    IPlayerRelationshipService relationshipService,
    ILogger<CommunitiesController> logger) : ControllerBase
{
    /// <summary>
    /// Get all detected player communities.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PlayerCommunity>>> GetCommunities(
        [FromQuery] int minSize = 3,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            var communities = await relationshipService.GetCommunitiesAsync(minSize, activeOnly);
            return Ok(communities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting communities");
            return StatusCode(500, "An error occurred while fetching communities");
        }
    }

    /// <summary>
    /// Get a specific community by ID.
    /// </summary>
    [HttpGet("{communityId}")]
    public async Task<ActionResult<PlayerCommunity>> GetCommunity(string communityId)
    {
        try
        {
            logger.LogInformation("GetCommunity called with ID: {CommunityId}", communityId);

            var community = await relationshipService.GetCommunityByIdAsync(communityId);

            if (community == null)
            {
                logger.LogWarning("Community not found: {CommunityId}. Getting all communities for debugging...", communityId);
                var allCommunities = await relationshipService.GetCommunitiesAsync(minSize: 0, activeOnly: false);
                var ids = string.Join(", ", allCommunities.Take(10).Select(c => c.Id));
                logger.LogWarning("Available community IDs (first 10): {Ids}. Total: {Total}", ids, allCommunities.Count);
                return NotFound($"Community {communityId} not found. Available: {ids}");
            }

            return Ok(community);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting community {CommunityId}", communityId);
            return StatusCode(500, "An error occurred while fetching community");
        }
    }

    /// <summary>
    /// Get communities that a player belongs to.
    /// </summary>
    [HttpGet("players/{playerName}")]
    public async Task<ActionResult<List<PlayerCommunity>>> GetPlayerCommunities(string playerName)
    {
        try
        {
            var communities = await relationshipService.GetPlayerCommunitiesAsync(playerName);
            return Ok(communities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting communities for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while fetching player communities");
        }
    }

    /// <summary>
    /// Manually trigger community detection (admin only).
    /// </summary>
    [HttpPost("detect")]
    public async Task<ActionResult<string>> DetectCommunities()
    {
        try
        {
            // TODO: Add authorization check for admin
            var result = await relationshipService.DetectAndStoreCommunities();
            return Ok(new { message = result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error detecting communities");
            return StatusCode(500, "An error occurred during community detection");
        }
    }
}