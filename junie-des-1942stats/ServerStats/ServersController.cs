using junie_des_1942stats.ServerStats.Models;
using Microsoft.AspNetCore.Mvc;

namespace junie_des_1942stats.ServerStats;

[ApiController]
[Route("stats/[controller]")]
public class ServersController : ControllerBase
{
    private readonly ServerStatsService _serverStatsService;

    public ServersController(ServerStatsService serverStatsService)
    {
        _serverStatsService = serverStatsService;
    }
    
    // Get detailed server statistics with optional days parameter
    [HttpGet("{serverName}")]
    public async Task<ActionResult<ServerStatistics>> GetServerStats(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");
            
        var stats = await _serverStatsService.GetServerStatistics(
            serverName,
            days ?? 7); // Default to 7 days if not specified
        
        if (string.IsNullOrEmpty(stats.ServerGuid))
            return NotFound($"Server '{serverName}' not found");
            
        return Ok(stats);
    }

    // Get server rankings with pagination
    [HttpGet("{serverName}/rankings")]
    public async Task<ActionResult<PagedResult<ServerRanking>>> GetServerRankings(
        string serverName,
        [FromQuery] int? year = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? playerName = null,
        [FromQuery] int? minScore = null,
        [FromQuery] int? minKills = null,
        [FromQuery] int? minDeaths = null,
        [FromQuery] double? minKdRatio = null,
        [FromQuery] int? minPlayTimeMinutes = null,
        [FromQuery] string? orderBy = "TotalScore",
        [FromQuery] string? orderDirection = "desc")
    {
        try
        {
            var result = await _serverStatsService.GetServerRankings(
                serverName, year, page, pageSize, playerName, 
                minScore, minKills, minDeaths, minKdRatio, minPlayTimeMinutes,
                orderBy, orderDirection);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Get statistics for a specific map on a server
    [HttpGet("{serverName}/maps/{mapName}")]
    public async Task<ActionResult<MapStatistics>> GetMapStats(string serverName, string mapName, [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");

        if (string.IsNullOrWhiteSpace(mapName))
            return BadRequest("Map name cannot be empty");

        var stats = await _serverStatsService.GetMapStatistics(
            serverName,
            mapName,
            days ?? 7); // Default to 7 days if not specified

        if (string.IsNullOrEmpty(stats.ServerGuid))
            return NotFound($"Server '{serverName}' not found");

        if (stats.TotalSessions == 0)
            return NotFound($"Map '{mapName}' not found on server '{serverName}'");

        return Ok(stats);
    }

    // Get round report by server, map, and date
    [HttpGet("round-report")]
    public async Task<ActionResult<SessionRoundReport>> GetRoundReport(
        [FromQuery] string serverGuid,
        [FromQuery] string mapName,
        [FromQuery] DateTime startTime)
    {
        if (string.IsNullOrWhiteSpace(serverGuid))
            return BadRequest("Server GUID is required");
        
        if (string.IsNullOrWhiteSpace(mapName))
            return BadRequest("Map name is required");

        try
        {
            var roundReport = await _serverStatsService.GetRoundReport(serverGuid, mapName, startTime);
            
            if (roundReport == null)
                return NotFound($"Round not found for server {serverGuid}, map {mapName} at {startTime}");

            return Ok(roundReport);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving round report: {ex.Message}");
        }
    }

    [HttpGet("{serverName}/insights")]
    public async Task<ActionResult<ServerInsights>> GetServerInsights(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");

        try
        {
            var insights = await _serverStatsService.GetServerInsights(
                serverName,
                days ?? 7); // Default to 7 days if not specified

            if (string.IsNullOrEmpty(insights.ServerGuid))
                return NotFound($"Server '{serverName}' not found");

            return Ok(insights);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}