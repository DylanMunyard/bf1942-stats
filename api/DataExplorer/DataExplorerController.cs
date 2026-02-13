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

        logger.LogDebug("Getting servers for data explorer with game filter: {Game}, page: {Page}, pageSize: {PageSize}",
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
        logger.LogDebug("Getting server detail for {ServerGuid}", serverGuid);

        var result = await dataExplorerService.GetServerDetailAsync(serverGuid);

        if (result == null)
        {
            logger.LogWarning("Server not found: {ServerGuid}", serverGuid);
            return NotFound($"Server '{serverGuid}' not found");
        }

        return Ok(result);
    }

    /// <summary>
    /// Get paginated map rotation for a specific server.
    /// </summary>
    /// <param name="serverGuid">The server GUID</param>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of results per page (default 10, max 100)</param>
    [HttpGet("servers/{serverGuid}/map-rotation")]
    [ProducesResponseType(typeof(MapRotationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MapRotationResponse>> GetServerMapRotation(
        string serverGuid,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        logger.LogDebug("Getting map rotation for server {ServerGuid}, page: {Page}, pageSize: {PageSize}",
            serverGuid, page, pageSize);

        var result = await dataExplorerService.GetServerMapRotationAsync(serverGuid, page, pageSize);

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
        logger.LogDebug("Getting maps for data explorer with game filter: {Game}", game);
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

        logger.LogDebug("Getting map detail for {MapName} with game filter: {Game}", mapName, game);

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

        logger.LogDebug("Getting server-map detail for {ServerGuid}/{MapName} with days={Days}",
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
        logger.LogDebug("Searching players with query: {Query} for game: {Game}", query, game);
        var result = await dataExplorerService.SearchPlayersAsync(query, game);
        return Ok(result);
    }

    /// <summary>
    /// Get player map rankings with per-server breakdown and rank information.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    /// <param name="serverGuid">Optional server GUID to filter results to a specific server</param>
    [HttpGet("players/{playerName}/maps")]
    [ProducesResponseType(typeof(PlayerMapRankingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerMapRankingsResponse>> GetPlayerMapRankings(
        string playerName,
        [FromQuery] string game = "bf1942",
        [FromQuery] int days = 60,
        [FromQuery] string? serverGuid = null)
    {
        // URL decode the player name
        playerName = Uri.UnescapeDataString(playerName);

        logger.LogDebug("Getting player map rankings for {PlayerName} with game: {Game}, days: {Days}, serverGuid: {ServerGuid}",
            playerName, game, days, serverGuid ?? "all");

        var result = await dataExplorerService.GetPlayerMapRankingsAsync(playerName, game, days, serverGuid);

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

        logger.LogDebug("Getting map activity patterns for {MapName} with game filter: {Game}", mapName, game);

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

        logger.LogDebug(
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

    /// <summary>
    /// Get randomized engagement statistics for a specific server to encourage exploration.
    /// Returns 3 different randomized interesting statistics about the server.
    /// </summary>
    [HttpGet("engagement/server/{serverGuid}")]
    [ProducesResponseType(typeof(ServerEngagementStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerEngagementStatsDto>> GetServerEngagementStats(string serverGuid)
    {
        logger.LogDebug("Getting randomized engagement stats for server: {ServerGuid}", serverGuid);

        var result = await dataExplorerService.GetServerEngagementStatsAsync(serverGuid);
        return Ok(result);
    }

    /// <summary>
    /// Get randomized engagement statistics for a specific player to encourage exploration.
    /// Returns 3 different randomized interesting statistics about the player.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    [HttpGet("engagement/player/{playerName}")]
    [ProducesResponseType(typeof(PlayerEngagementStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerEngagementStatsDto>> GetPlayerEngagementStats(
        string playerName,
        [FromQuery] string game = "bf1942")
    {
        // URL decode the player name
        playerName = Uri.UnescapeDataString(playerName);

        logger.LogDebug("Getting randomized engagement stats for player: {PlayerName} in game: {Game}", playerName, game);

        var result = await dataExplorerService.GetPlayerEngagementStatsAsync(playerName, game);
        return Ok(result);
    }

    /// <summary>
    /// Get sliced player statistics with configurable dimensions and pagination.
    /// Enables advanced data exploration by different metrics and groupings.
    /// Returns an empty result set if no data is available, preserving player context.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="sliceType">The slice dimension type (e.g., winsByMap, scoreByMap, etc.)</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based, default 1)</param>
    /// <param name="pageSize">Number of results per page (default 20, max 100)</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    [HttpGet("players/{playerName}/sliced-stats")]
    [ProducesResponseType(typeof(PlayerSlicedStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlayerSlicedStatsResponse>> GetPlayerSlicedStats(
        string playerName,
        [FromQuery] string sliceType,
        [FromQuery] string game = "bf1942",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int days = 60)
    {
        // URL decode the player name
        playerName = Uri.UnescapeDataString(playerName);

        // Parse slice type
        if (!Enum.TryParse<SliceDimensionType>(sliceType, true, out var parsedSliceType))
        {
            logger.LogWarning("Invalid slice type provided: {SliceType}", sliceType);
            return BadRequest($"Invalid slice type '{sliceType}'. Valid types are: " +
                string.Join(", ", Enum.GetNames<SliceDimensionType>()));
        }

        // Clamp parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        logger.LogDebug(
            "Getting sliced player stats for {PlayerName} with sliceType: {SliceType}, game: {Game}, page: {Page}, pageSize: {PageSize}, days: {Days}",
            playerName, parsedSliceType, game, page, pageSize, days);

        var result = await dataExplorerService.GetPlayerSlicedStatsAsync(
            playerName, parsedSliceType, game, page, pageSize, days);

        // Always return the result (even if empty) to preserve player context
        if (result.Results.Count == 0)
        {
            logger.LogInformation("No data found for player {PlayerName} in game {Game} with slice type {SliceType}, returning empty result set",
                playerName, game, parsedSliceType);
        }

        return Ok(result);
    }

    /// <summary>
    /// Get available slice dimensions for the player data explorer.
    /// </summary>
    [HttpGet("slice-dimensions")]
    [ProducesResponseType(typeof(List<SliceDimensionOption>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SliceDimensionOption>>> GetSliceDimensions()
    {
        logger.LogDebug("Getting available slice dimensions for player data explorer");

        var result = await dataExplorerService.GetAvailableSliceDimensionsAsync();
        return Ok(result);
    }
}
