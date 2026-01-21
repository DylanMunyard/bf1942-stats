using api.DataExplorer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.DataExplorer;

[ApiController]
[Route("stats/data-explorer")]
public class DataExplorerController(
    IDataExplorerService dataExplorerService,
    ILogger<DataExplorerController> logger) : ControllerBase
{
    /// <summary>
    /// Get all servers with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("servers")]
    [ProducesResponseType(typeof(ServerListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServerListResponse>> GetServers([FromQuery] string game = "bf1942")
    {
        logger.LogInformation("Getting servers for data explorer with game filter: {Game}", game);
        var result = await dataExplorerService.GetServersAsync(game);
        return Ok(result);
    }

    /// <summary>
    /// Get detailed information for a specific server.
    /// </summary>
    [HttpGet("servers/{serverGuid}")]
    [ProducesResponseType(typeof(ServerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerDetailDto>> GetServerDetail(string serverGuid)
    {
        logger.LogInformation("Getting server detail for {ServerGuid}", serverGuid);

        var result = await dataExplorerService.GetServerDetailAsync(serverGuid);

        if (result == null)
        {
            logger.LogWarning("Server not found: {ServerGuid}", serverGuid);
            return NotFound($"Server '{serverGuid}' not found");
        }

        return Ok(result);
    }

    /// <summary>
    /// Get all maps with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("maps")]
    [ProducesResponseType(typeof(MapListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MapListResponse>> GetMaps([FromQuery] string game = "bf1942")
    {
        logger.LogInformation("Getting maps for data explorer with game filter: {Game}", game);
        var result = await dataExplorerService.GetMapsAsync(game);
        return Ok(result);
    }

    /// <summary>
    /// Get detailed information for a specific map, filtered by game.
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("maps/{mapName}")]
    [ProducesResponseType(typeof(MapDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapDetailDto>> GetMapDetail(string mapName, [FromQuery] string game = "bf1942")
    {
        // URL decode the map name
        mapName = Uri.UnescapeDataString(mapName);

        logger.LogInformation("Getting map detail for {MapName} with game filter: {Game}", mapName, game);

        var result = await dataExplorerService.GetMapDetailAsync(mapName, game);

        if (result == null)
        {
            logger.LogWarning("Map not found: {MapName} for game: {Game}", mapName, game);
            return NotFound($"Map '{mapName}' not found for game '{game}'");
        }

        return Ok(result);
    }


    /// <summary>
    /// Get detailed information for a specific server-map combination.
    /// </summary>
    [HttpGet("servers/{serverGuid}/maps/{mapName}")]
    [ProducesResponseType(typeof(ServerMapDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerMapDetailDto>> GetServerMapDetail(
        string serverGuid, 
        string mapName,
        [FromQuery] int days = 60)
    {
        // URL decode the map name
        mapName = Uri.UnescapeDataString(mapName);

        logger.LogInformation("Getting server-map detail for {ServerGuid}/{MapName} with days={Days}", 
            serverGuid, mapName, days);

        var result = await dataExplorerService.GetServerMapDetailAsync(serverGuid, mapName, days);

        if (result == null)
        {
            logger.LogWarning("Server-map combination not found: {ServerGuid}/{MapName}", serverGuid, mapName);
            return NotFound($"No data found for server '{serverGuid}' and map '{mapName}'");
        }

        return Ok(result);
    }
}
