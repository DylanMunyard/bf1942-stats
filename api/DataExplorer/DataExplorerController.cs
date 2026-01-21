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
    /// Get all servers with summary information.
    /// </summary>
    [HttpGet("servers")]
    [ProducesResponseType(typeof(ServerListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServerListResponse>> GetServers()
    {
        logger.LogInformation("Getting all servers for data explorer");
        var result = await dataExplorerService.GetServersAsync();
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
    /// Get all maps with summary information.
    /// </summary>
    [HttpGet("maps")]
    [ProducesResponseType(typeof(MapListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MapListResponse>> GetMaps()
    {
        logger.LogInformation("Getting all maps for data explorer");
        var result = await dataExplorerService.GetMapsAsync();
        return Ok(result);
    }

    /// <summary>
    /// Get detailed information for a specific map.
    /// </summary>
    [HttpGet("maps/{mapName}")]
    [ProducesResponseType(typeof(MapDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapDetailDto>> GetMapDetail(string mapName)
    {
        // URL decode the map name
        mapName = Uri.UnescapeDataString(mapName);

        logger.LogInformation("Getting map detail for {MapName}", mapName);

        var result = await dataExplorerService.GetMapDetailAsync(mapName);

        if (result == null)
        {
            logger.LogWarning("Map not found: {MapName}", mapName);
            return NotFound($"Map '{mapName}' not found");
        }

        return Ok(result);
    }
}
