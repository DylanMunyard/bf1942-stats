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
        [FromQuery] string game, 
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");
            
        var stats = await _serverStatsService.GetServerStatistics(
            serverName, 
            game,
            days ?? 7); // Default to 7 days if not specified
        
        if (string.IsNullOrEmpty(stats.ServerGuid))
            return NotFound($"Server '{serverName}' not found");
            
        return Ok(stats);
    }

    // Get server rankings with pagination
    [HttpGet("{serverName}/rankings")]
    public async Task<ActionResult<PagedResult<ServerRanking>>> GetServerRankings(
        string serverName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        try
        {
            var result = await _serverStatsService.GetServerRankings(serverName, page, pageSize);
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
}