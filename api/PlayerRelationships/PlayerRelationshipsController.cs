using api.PlayerRelationships.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

[ApiController]
[Route("stats/relationships")]
public class PlayerRelationshipsController(
    IPlayerRelationshipService relationshipService,
    ILogger<PlayerRelationshipsController> logger) : ControllerBase
{
    /// <summary>
    /// Get a player's most frequent teammates.
    /// </summary>
    [HttpGet("players/{playerName}/teammates")]
    public async Task<ActionResult<List<PlayerRelationship>>> GetTeammates(
        string playerName,
        [FromQuery] int limit = 20)
    {
        try
        {
            var teammates = await relationshipService.GetMostFrequentCoPlayersAsync(playerName, limit);
            return Ok(teammates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting teammates for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while fetching teammates");
        }
    }

    /// <summary>
    /// Get potential new connections for a player (squad finder).
    /// </summary>
    [HttpGet("players/{playerName}/potential-connections")]
    public async Task<ActionResult<List<string>>> GetPotentialConnections(
        string playerName,
        [FromQuery] int limit = 20,
        [FromQuery] int daysActive = 30)
    {
        try
        {
            var connections = await relationshipService.GetPotentialConnectionsAsync(
                playerName, limit, daysActive);
            return Ok(connections);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting potential connections for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while finding potential connections");
        }
    }

    /// <summary>
    /// Get the relationship between two specific players.
    /// </summary>
    [HttpGet("players/{player1}/relationship/{player2}")]
    public async Task<ActionResult<PlayerRelationship>> GetRelationship(
        string player1,
        string player2)
    {
        try
        {
            var relationship = await relationshipService.GetRelationshipAsync(player1, player2);
            
            if (relationship == null)
                return NotFound($"No relationship found between {player1} and {player2}");
                
            return Ok(relationship);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting relationship between {Player1} and {Player2}", 
                player1, player2);
            return StatusCode(500, "An error occurred while fetching relationship");
        }
    }

    /// <summary>
    /// Get shared servers between two players.
    /// </summary>
    [HttpGet("players/{player1}/shared-servers/{player2}")]
    public async Task<ActionResult<List<string>>> GetSharedServers(
        string player1,
        string player2)
    {
        try
        {
            var servers = await relationshipService.GetSharedServersAsync(player1, player2);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting shared servers for {Player1} and {Player2}", 
                player1, player2);
            return StatusCode(500, "An error occurred while fetching shared servers");
        }
    }

    /// <summary>
    /// Get a player's recent new connections.
    /// </summary>
    [HttpGet("players/{playerName}/recent-connections")]
    public async Task<ActionResult<List<PlayerRelationship>>> GetRecentConnections(
        string playerName,
        [FromQuery] int daysSince = 7)
    {
        try
        {
            var connections = await relationshipService.GetRecentConnectionsAsync(playerName, daysSince);
            return Ok(connections);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting recent connections for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while fetching recent connections");
        }
    }

    /// <summary>
    /// Get network statistics for a player.
    /// </summary>
    [HttpGet("players/{playerName}/network-stats")]
    public async Task<ActionResult<PlayerNetworkStats>> GetNetworkStats(string playerName)
    {
        try
        {
            var stats = await relationshipService.GetPlayerNetworkStatsAsync(playerName);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting network stats for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while fetching network statistics");
        }
    }

    /// <summary>
    /// Get a player's network graph for visualization.
    /// </summary>
    [HttpGet("players/{playerName}/network-graph")]
    public async Task<ActionResult<PlayerNetworkGraph>> GetNetworkGraph(
        string playerName,
        [FromQuery] int depth = 2,
        [FromQuery] int maxNodes = 100)
    {
        try
        {
            // Limit depth to prevent expensive queries
            depth = Math.Min(depth, 3);
            maxNodes = Math.Min(maxNodes, 200);

            var graph = await relationshipService.GetPlayerNetworkGraphAsync(
                playerName, depth, maxNodes);
            return Ok(graph);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting network graph for {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while fetching network graph");
        }
    }

    /// <summary>
    /// Get server social analytics.
    /// </summary>
    [HttpGet("servers/{serverGuid}/social-stats")]
    public async Task<ActionResult<ServerSocialStats>> GetServerSocialStats(string serverGuid)
    {
        try
        {
            var stats = await GetServerSocialStatsAsync(serverGuid);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting social stats for server {ServerGuid}", serverGuid);
            return StatusCode(500, "An error occurred while fetching server social statistics");
        }
    }

    private async Task<ServerSocialStats> GetServerSocialStatsAsync(string serverGuid)
    {
        // TODO: Implement server social analytics
        // This would query Neo4j for:
        // - Number of unique players
        // - Average connections per player
        // - Community detection
        // - Player retention metrics
        
        return new ServerSocialStats
        {
            ServerGuid = serverGuid,
            UniquePlayerCount = 0,
            AverageConnectionsPerPlayer = 0,
            CommunityCount = 0,
            RetentionRate = 0
        };
    }
}