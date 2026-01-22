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
    /// Get paginated servers with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of results per page (default 50, max 100)</param>
    [HttpGet("servers")]
    [ProducesResponseType(typeof(ServerListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServerListResponse>> GetServers(
        [FromQuery] string game = "bf1942",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // Clamp page and page size
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        
        logger.LogInformation("Getting servers for data explorer with game filter: {Game}, page: {Page}, pageSize: {PageSize}", 
            game, page, pageSize);
        var result = await dataExplorerService.GetServersAsync(game, page, pageSize);
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

    /// <summary>
    /// Search for players by name prefix.
    /// Requires at least 3 characters. Returns top 50 matches by score.
    /// </summary>
    /// <param name="query">Search query (min 3 characters)</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("players/search")]
    [ProducesResponseType(typeof(PlayerSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlayerSearchResponse>> SearchPlayers(
        [FromQuery] string query,
        [FromQuery] string game = "bf1942")
    {
        logger.LogInformation("Searching players with query: {Query} for game: {Game}", query, game);
        var result = await dataExplorerService.SearchPlayersAsync(query, game);
        return Ok(result);
    }

    /// <summary>
    /// Get player map rankings with per-server breakdown and rank information.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    [HttpGet("players/{playerName}/maps")]
    [ProducesResponseType(typeof(PlayerMapRankingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerMapRankingsResponse>> GetPlayerMapRankings(
        string playerName,
        [FromQuery] string game = "bf1942",
        [FromQuery] int days = 60)
    {
        // URL decode the player name
        playerName = Uri.UnescapeDataString(playerName);

        logger.LogInformation("Getting player map rankings for {PlayerName} with game: {Game}, days: {Days}",
            playerName, game, days);

        var result = await dataExplorerService.GetPlayerMapRankingsAsync(playerName, game, days);

        if (result == null)
        {
            logger.LogWarning("Player not found or no data: {PlayerName} for game: {Game}", playerName, game);
            return NotFound($"No data found for player '{playerName}' in game '{game}'");
        }

        return Ok(result);
    }

    /// <summary>
    /// Get activity patterns for a specific map showing when it's typically played.
    /// Returns hourly patterns grouped by day of week for heatmap visualization.
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("maps/{mapName}/activity-patterns")]
    [ProducesResponseType(typeof(MapActivityPatternsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapActivityPatternsResponse>> GetMapActivityPatterns(
        string mapName,
        [FromQuery] string game = "bf1942")
    {
        // URL decode the map name
        mapName = Uri.UnescapeDataString(mapName);

        logger.LogInformation("Getting map activity patterns for {MapName} with game filter: {Game}", mapName, game);

        var result = await dataExplorerService.GetMapActivityPatternsAsync(mapName, game);

        if (result == null)
        {
            logger.LogWarning("No activity patterns found for map: {MapName} in game: {Game}", mapName, game);
            return NotFound($"No activity patterns found for map '{mapName}' in game '{game}'");
        }

        return Ok(result);
    }

    /// <summary>
    /// Get paginated player rankings for a specific map (aggregated across all servers).
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of results per page (default 10, max 50)</param>
    /// <param name="search">Optional player name search filter (min 2 characters)</param>
    /// <param name="serverGuid">Optional server GUID filter</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    /// <param name="sortBy">Sort field: score (default), kills, kdRatio, killRate</param>
    [HttpGet("maps/{mapName}/rankings")]
    [ProducesResponseType(typeof(MapPlayerRankingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapPlayerRankingsResponse>> GetMapPlayerRankings(
        string mapName,
        [FromQuery] string game = "bf1942",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? serverGuid = null,
        [FromQuery] int days = 60,
        [FromQuery] string sortBy = "score")
    {
        // URL decode the map name
        mapName = Uri.UnescapeDataString(mapName);

        // Clamp page size
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Max(1, page);

        // Validate sortBy
        var validSortFields = new[] { "score", "kills", "kdRatio", "killRate" };
        if (!validSortFields.Contains(sortBy.ToLowerInvariant()))
            sortBy = "score";

        logger.LogInformation(
            "Getting map player rankings for {MapName} with game: {Game}, page: {Page}, pageSize: {PageSize}, search: {Search}, serverGuid: {ServerGuid}, sortBy: {SortBy}",
            mapName, game, page, pageSize, search, serverGuid, sortBy);

        var result = await dataExplorerService.GetMapPlayerRankingsAsync(
            mapName, game, page, pageSize, search, serverGuid, days, sortBy);

        if (result == null)
        {
            logger.LogWarning("Map not found or no data: {MapName} for game: {Game}", mapName, game);
            return NotFound($"No data found for map '{mapName}' in game '{game}'");
        }

        return Ok(result);
    }
}
