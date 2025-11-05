using api.ServerStats.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using api.Constants;
using Microsoft.AspNetCore.Http;

namespace api.ServerStats;

[ApiController]
[Route("stats/[controller]")]
public class ServersController : ControllerBase
{
    private readonly IServerStatsService _serverStatsService;
    private readonly ILogger<ServersController> _logger;

    public ServersController(IServerStatsService serverStatsService, ILogger<ServersController> logger)
    {
        _serverStatsService = serverStatsService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves detailed statistics for a specific server.
    /// </summary>
    /// <param name="serverName">The URL-encoded name of the server.</param>
    /// <param name="days">Optional number of days to include in statistics (default: 7).</param>
    /// <returns>Server statistics including player counts, map rotation, and performance metrics.</returns>
    /// <response code="200">Returns the server statistics.</response>
    /// <response code="400">If the server name is empty or invalid.</response>
    /// <response code="404">If the server is not found in the database.</response>
    [HttpGet("{serverName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerStatistics>> GetServerStats(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest(ApiConstants.ValidationMessages.ServerNameEmpty);

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Looking up server statistics for server name: '{ServerName}'", serverName);

        var stats = await _serverStatsService.GetServerStatistics(
            serverName,
            days ?? ApiConstants.TimePeriods.DefaultDays);

        if (string.IsNullOrEmpty(stats.ServerGuid))
        {
            _logger.LogWarning("Server not found in database: '{ServerName}'", serverName);
            return NotFound($"Server '{serverName}' not found");
        }

        return Ok(stats);
    }

    // Get server leaderboards for a specific time period
    [HttpGet("{serverName}/leaderboards")]
    public async Task<ActionResult<ServerLeaderboards>> GetServerLeaderboards(
        string serverName,
        [FromQuery] int days = ApiConstants.TimePeriods.DefaultDays,
        [FromQuery] int? minPlayersForWeighting = null)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest(ApiConstants.ValidationMessages.ServerNameEmpty);

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Getting server leaderboards for '{ServerName}' with {Days} days", serverName, days);

        try
        {
            var leaderboards = await _serverStatsService.GetServerLeaderboards(
                serverName,
                days,
                minPlayersForWeighting);

            if (string.IsNullOrEmpty(leaderboards.ServerGuid))
            {
                _logger.LogWarning("Server not found in database: '{ServerName}'", serverName);
                return NotFound($"Server '{serverName}' not found");
            }

            return Ok(leaderboards);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
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
            // Use modern URL decoding that preserves + signs
            serverName = Uri.UnescapeDataString(serverName);

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


    [HttpGet("{serverName}/insights")]
    public async Task<ActionResult<ServerInsights>> GetServerInsights(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest(ApiConstants.ValidationMessages.ServerNameEmpty);

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Looking up server insights for server name: '{ServerName}' with days: {Days}", serverName, days);

        try
        {
            var insights = await _serverStatsService.GetServerInsights(
                serverName,
                days ?? ApiConstants.TimePeriods.DefaultDays);

            if (string.IsNullOrEmpty(insights.ServerGuid))
            {
                _logger.LogWarning("Server not found: '{ServerName}'", serverName);
                return NotFound($"Server '{serverName}' not found");
            }

            return Ok(insights);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{serverName}/insights/maps")]
    public async Task<ActionResult<ServerMapsInsights>> GetServerMapsInsights(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest(ApiConstants.ValidationMessages.ServerNameEmpty);

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Looking up server maps insights for server name: '{ServerName}' with days: {Days}", serverName, days);

        try
        {
            var mapsInsights = await _serverStatsService.GetServerMapsInsights(
                serverName,
                days ?? ApiConstants.TimePeriods.DefaultDays);

            if (string.IsNullOrEmpty(mapsInsights.ServerGuid))
            {
                _logger.LogWarning("Server not found: '{ServerName}'", serverName);
                return NotFound($"Server '{serverName}' not found");
            }

            return Ok(mapsInsights);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Get all servers with pagination and filtering
    [HttpGet]
    public async Task<ActionResult<PagedResult<ServerBasicInfo>>> GetAllServers(
        [FromQuery] int page = ApiConstants.Pagination.DefaultPage,
        [FromQuery] int pageSize = ApiConstants.Pagination.DefaultPageSize,
        [FromQuery] string sortBy = ApiConstants.ServerSortFields.ServerName,
        [FromQuery] string sortOrder = ApiConstants.Sorting.AscendingOrder,
        [FromQuery] string? serverName = null,
        [FromQuery] string? gameId = null,
        [FromQuery] string? game = null,
        [FromQuery] string? country = null,
        [FromQuery] string? region = null,
        [FromQuery] bool? hasActivePlayers = null,
        [FromQuery] DateTime? lastActivityFrom = null,
        [FromQuery] DateTime? lastActivityTo = null,
        [FromQuery] int? minTotalPlayers = null,
        [FromQuery] int? maxTotalPlayers = null,
        [FromQuery] int? minActivePlayersLast24h = null,
        [FromQuery] int? maxActivePlayersLast24h = null)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest(ApiConstants.ValidationMessages.PageNumberTooLow);

        if (pageSize < 1 || pageSize > ApiConstants.Pagination.MaxPageSize)
            return BadRequest(ApiConstants.ValidationMessages.PageSizeTooLarge(ApiConstants.Pagination.MaxPageSize));

        if (!ApiConstants.ServerSortFields.ValidFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest(ApiConstants.ValidationMessages.InvalidSortField(
                string.Join(", ", ApiConstants.ServerSortFields.ValidFields)));

        if (!ApiConstants.Sorting.ValidSortOrders.Contains(sortOrder.ToLower()))
            return BadRequest(ApiConstants.ValidationMessages.InvalidSortOrder);

        // Validate filter parameters
        if (minTotalPlayers.HasValue && minTotalPlayers < 0)
            return BadRequest(ApiConstants.ValidationMessages.MinimumTotalPlayersNegative);

        if (maxTotalPlayers.HasValue && maxTotalPlayers < 0)
            return BadRequest(ApiConstants.ValidationMessages.MaximumTotalPlayersNegative);

        if (minTotalPlayers.HasValue && maxTotalPlayers.HasValue && minTotalPlayers > maxTotalPlayers)
            return BadRequest(ApiConstants.ValidationMessages.MinimumTotalPlayersGreaterThanMaximum);

        if (minActivePlayersLast24h.HasValue && minActivePlayersLast24h < 0)
            return BadRequest(ApiConstants.ValidationMessages.MinimumActivePlayersNegative);

        if (maxActivePlayersLast24h.HasValue && maxActivePlayersLast24h < 0)
            return BadRequest(ApiConstants.ValidationMessages.MaximumActivePlayersNegative);

        if (minActivePlayersLast24h.HasValue && maxActivePlayersLast24h.HasValue && minActivePlayersLast24h > maxActivePlayersLast24h)
            return BadRequest(ApiConstants.ValidationMessages.MinimumActivePlayersGreaterThanMaximum);

        if (lastActivityFrom.HasValue && lastActivityTo.HasValue && lastActivityFrom > lastActivityTo)
            return BadRequest(ApiConstants.ValidationMessages.LastActivityFromGreaterThanTo("LastActivity"));

        // Validate game parameter if provided
        if (!string.IsNullOrWhiteSpace(game))
        {
            if (!ApiConstants.Games.AllowedGames.Contains(game.ToLower()))
                return BadRequest(ApiConstants.ValidationMessages.InvalidGame(
                    string.Join(", ", ApiConstants.Games.AllowedGames)));
        }

        try
        {
            var filters = new ServerFilters
            {
                ServerName = serverName?.Trim(),
                GameId = gameId?.Trim(),
                Game = game?.Trim().ToLower(),
                Country = country?.Trim(),
                Region = region?.Trim(),
                HasActivePlayers = hasActivePlayers,
                LastActivityFrom = lastActivityFrom,
                LastActivityTo = lastActivityTo,
                MinTotalPlayers = minTotalPlayers,
                MaxTotalPlayers = maxTotalPlayers,
                MinActivePlayersLast24h = minActivePlayersLast24h,
                MaxActivePlayersLast24h = maxActivePlayersLast24h
            };

            var result = await _serverStatsService.GetAllServersWithPaging(page, pageSize, sortBy, sortOrder, filters);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Search servers by name with pagination
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<ServerBasicInfo>>> SearchServers(
        [FromQuery] string query,
        [FromQuery] string? game = null,
        [FromQuery] int page = ApiConstants.Pagination.DefaultPage,
        [FromQuery] int pageSize = ApiConstants.Pagination.SearchDefaultPageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(ApiConstants.ValidationMessages.SearchQueryEmpty);

        if (page < 1)
            return BadRequest(ApiConstants.ValidationMessages.PageNumberTooLow);

        if (pageSize < 1 || pageSize > ApiConstants.Pagination.SearchMaxPageSize)
            return BadRequest(ApiConstants.ValidationMessages.PageSizeTooLarge(ApiConstants.Pagination.SearchMaxPageSize));

        // Validate game parameter if provided
        if (!string.IsNullOrWhiteSpace(game))
        {
            if (!ApiConstants.Games.AllowedGames.Contains(game.ToLower()))
                return BadRequest(ApiConstants.ValidationMessages.InvalidGame(
                    string.Join(", ", ApiConstants.Games.AllowedGames)));
        }

        try
        {
            var filters = new ServerFilters
            {
                ServerName = query.Trim(),
                Game = game?.Trim().ToLower()
            };

            var result = await _serverStatsService.GetAllServersWithPaging(
                page, pageSize, "ServerName", "asc", filters);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
