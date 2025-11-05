using api.PlayerStats.Models;
using Microsoft.AspNetCore.Mvc;
using api.ClickHouse;
using api.ClickHouse.Models;
using api.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace api.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly IPlayerStatsService _playerStatsService;
    private readonly IServerStatisticsService _serverStatisticsService;
    private readonly IPlayerComparisonService _playerComparisonService;
    private readonly PlayerRoundsReadService _playerRoundsService;
    private readonly ILogger<PlayersController> _logger;

    public PlayersController(IPlayerStatsService playerStatsService, IServerStatisticsService serverStatisticsService, IPlayerComparisonService playerComparisonService, PlayerRoundsReadService playerRoundsService, ILogger<PlayersController> logger)
    {
        _playerStatsService = playerStatsService;
        _serverStatisticsService = serverStatisticsService;
        _playerComparisonService = playerComparisonService;
        _playerRoundsService = playerRoundsService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a paginated list of all players with optional filtering and sorting.
    /// </summary>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Number of items per page (1-500, default: 50).</param>
    /// <param name="sortBy">Field to sort by. Valid options: PlayerName, TotalPlayTimeMinutes, LastSeen, IsActive.</param>
    /// <param name="sortOrder">Sort order: asc or desc (default: desc).</param>
    /// <param name="playerName">Optional player name to filter by.</param>
    /// <param name="minPlayTime">Optional minimum play time in minutes.</param>
    /// <param name="maxPlayTime">Optional maximum play time in minutes.</param>
    /// <param name="lastSeenFrom">Optional start date for last seen filter.</param>
    /// <param name="lastSeenTo">Optional end date for last seen filter.</param>
    /// <param name="isActive">Optional active status filter.</param>
    /// <param name="serverName">Optional server name filter.</param>
    /// <param name="gameId">Optional game ID filter.</param>
    /// <param name="game">Optional game type filter (bf1942, fh2, bfvietnam).</param>
    /// <param name="mapName">Optional map name filter.</param>
    /// <returns>Paginated list of players matching the specified criteria.</returns>
    /// <response code="200">Returns the list of players.</response>
    /// <response code="400">If pagination or filter parameters are invalid.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PlayerBasicInfo>>> GetAllPlayers(
        [FromQuery] int page = ApiConstants.Pagination.DefaultPage,
        [FromQuery] int pageSize = ApiConstants.Pagination.DefaultPageSize,
        [FromQuery] string sortBy = ApiConstants.PlayerSortFields.IsActive,
        [FromQuery] string sortOrder = ApiConstants.Sorting.DescendingOrder,
        [FromQuery] string? playerName = null,
        [FromQuery] int? minPlayTime = null,
        [FromQuery] int? maxPlayTime = null,
        [FromQuery] DateTime? lastSeenFrom = null,
        [FromQuery] DateTime? lastSeenTo = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? serverName = null,
        [FromQuery] string? gameId = null,
        [FromQuery] string? game = null,
        [FromQuery] string? mapName = null)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest(ApiConstants.ValidationMessages.PageNumberTooLow);

        if (pageSize < 1 || pageSize > ApiConstants.Pagination.MaxPageSize)
            return BadRequest(ApiConstants.ValidationMessages.PageSizeTooLarge(ApiConstants.Pagination.MaxPageSize));

        if (!ApiConstants.PlayerSortFields.ValidFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest(ApiConstants.ValidationMessages.InvalidSortField(
                string.Join(", ", ApiConstants.PlayerSortFields.ValidFields)));

        if (!ApiConstants.Sorting.ValidSortOrders.Contains(sortOrder.ToLower()))
            return BadRequest(ApiConstants.ValidationMessages.InvalidSortOrder);

        // Validate filter parameters
        if (minPlayTime.HasValue && minPlayTime < 0)
            return BadRequest(ApiConstants.ValidationMessages.MinimumPlayTimeNegative);

        if (maxPlayTime.HasValue && maxPlayTime < 0)
            return BadRequest(ApiConstants.ValidationMessages.MaximumPlayTimeNegative);

        if (minPlayTime.HasValue && maxPlayTime.HasValue && minPlayTime > maxPlayTime)
            return BadRequest(ApiConstants.ValidationMessages.MinimumPlayTimeGreaterThanMaximum);

        if (lastSeenFrom.HasValue && lastSeenTo.HasValue && lastSeenFrom > lastSeenTo)
            return BadRequest(ApiConstants.ValidationMessages.LastActivityFromGreaterThanTo("LastSeen"));

        try
        {
            var filters = new PlayerFilters
            {
                PlayerName = playerName?.Trim(),
                MinPlayTime = minPlayTime,
                MaxPlayTime = maxPlayTime,
                LastSeenFrom = lastSeenFrom,
                LastSeenTo = lastSeenTo,
                IsActive = isActive,
                ServerName = serverName,
                GameId = gameId,
                Game = game,
                MapName = mapName
            };

            var result = await _playerStatsService.GetAllPlayersWithPaging(page, pageSize, sortBy, sortOrder, filters);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Get detailed player statistics
    [HttpGet("{playerName}")]
    public async Task<ActionResult<PlayerTimeStatistics>> GetPlayerStats(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest(ApiConstants.ValidationMessages.PlayerNameEmpty);

        // Use modern URL decoding that preserves + signs
        playerName = Uri.UnescapeDataString(playerName);

        var stats = await _playerStatsService.GetPlayerStatistics(playerName);

        // Copy ranking from insights.ServerRankings to servers array
        foreach (var server in stats.Servers)
        {
            server.Ranking = stats.Insights.ServerRankings
                .FirstOrDefault(r => r.ServerGuid == server.ServerGuid);
        }

        // Ensure recentStats is not null before returning
        if (stats.RecentStats == null)
        {
            stats.RecentStats = new Models.RecentStats();
        }

        return Ok(stats);
    }


    // Get session details
    [HttpGet("{playerName}/sessions/{sessionId}")]
    public async Task<ActionResult<SessionDetail>> GetPlayerStats(string playerName, int sessionId)
    {
        // Use modern URL decoding that preserves + signs
        playerName = Uri.UnescapeDataString(playerName);

        var stats = await _playerStatsService.GetSession(playerName, sessionId);

        if (stats is null)
        {
            return NotFound($"Session '{sessionId}' not found");
        }

        return Ok(stats);
    }

    // Get server-specific map statistics for a player
    [HttpGet("{playerName}/server/{serverGuid}/mapstats")]
    public async Task<ActionResult<List<ServerStatistics>>> GetPlayerServerMapStats(
        string playerName,
        string serverGuid,
        [FromQuery] string range = "ThisYear")
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest(ApiConstants.ValidationMessages.PlayerNameEmpty);

        if (string.IsNullOrWhiteSpace(serverGuid))
            return BadRequest(ApiConstants.ValidationMessages.ServerGuidEmpty);

        // Use modern URL decoding that preserves + signs
        playerName = Uri.UnescapeDataString(playerName);

        if (!Enum.TryParse<TimePeriod>(range, true, out var period))
            return BadRequest($"Invalid range. Valid options: {string.Join(", ", Enum.GetNames<TimePeriod>())}");

        try
        {
            var stats = await _serverStatisticsService.GetServerStats(playerName, period, serverGuid);

            if (!stats.Any())
                return NotFound($"No statistics found for player '{playerName}' on server '{serverGuid}' for the specified period");

            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while retrieving server statistics: {ex.Message}");
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<PlayerBasicInfo>>> SearchPlayers(
        [FromQuery] string query,
        [FromQuery] int page = ApiConstants.Pagination.DefaultPage,
        [FromQuery] int pageSize = ApiConstants.Pagination.SearchDefaultPageSize)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(ApiConstants.ValidationMessages.SearchQueryEmpty);

        if (page < 1)
            return BadRequest(ApiConstants.ValidationMessages.PageNumberTooLow);

        if (pageSize < 1 || pageSize > ApiConstants.Pagination.SearchMaxPageSize)
            return BadRequest(ApiConstants.ValidationMessages.PageSizeTooLarge(ApiConstants.Pagination.SearchMaxPageSize));

        try
        {
            var filters = new PlayerFilters
            {
                PlayerName = query.Trim()
            };

            var result = await _playerStatsService.GetAllPlayersWithPaging(
                page, pageSize, "PlayerName", "asc", filters);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("compare")]
    public async Task<IActionResult> ComparePlayers([FromQuery] string player1, [FromQuery] string player2, [FromQuery] string? serverGuid = null)
    {
        if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
            return BadRequest(ApiConstants.ValidationMessages.BothPlayersRequired);

        // Use modern URL decoding that preserves + signs
        player1 = Uri.UnescapeDataString(player1);
        player2 = Uri.UnescapeDataString(player2);

        try
        {
            var result = await _playerComparisonService.ComparePlayersAsync(player1, player2, serverGuid);
            return Ok(result);
        }
        catch (Exception ex)
        {
            // Log the exception
            _logger.LogError(ex, "Error comparing players {Player1} and {Player2}", player1, player2);
            // Return a generic 500 error
            return StatusCode(500, "An internal server error occurred while comparing players.");
        }
    }

    [HttpGet("{playerName}/similar")]
    public async Task<IActionResult> GetSimilarPlayers(string playerName, [FromQuery] int limit = ApiConstants.SimilaritySearch.DefaultLimit, [FromQuery] string mode = "default")
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest(ApiConstants.ValidationMessages.PlayerNameEmpty);

        // Use modern URL decoding that preserves + signs
        playerName = Uri.UnescapeDataString(playerName);

        if (limit < ApiConstants.SimilaritySearch.MinLimit || limit > ApiConstants.SimilaritySearch.MaxLimit)
            return BadRequest(ApiConstants.ValidationMessages.LimitOutOfRange);

        // Parse similarity mode
        if (!Enum.TryParse<SimilarityMode>(mode, true, out var similarityMode))
            return BadRequest($"Invalid mode. Valid options: {string.Join(", ", Enum.GetNames<SimilarityMode>())}");

        try
        {
            var result = await _playerComparisonService.FindSimilarPlayersAsync(playerName, limit, true, similarityMode);

            if (result.TargetPlayerStats == null)
                return NotFound($"Player '{playerName}' not found or has insufficient data");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar players for {PlayerName} with mode {Mode}", playerName, mode);
            return StatusCode(500, "An internal server error occurred while finding similar players.");
        }
    }

    [HttpGet("compare/activity-hours")]
    public async Task<IActionResult> ComparePlayersActivityHours([FromQuery] string player1, [FromQuery] string player2)
    {
        if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
            return BadRequest(ApiConstants.ValidationMessages.BothPlayersRequired);

        // Use modern URL decoding that preserves + signs
        player1 = Uri.UnescapeDataString(player1);
        player2 = Uri.UnescapeDataString(player2);

        try
        {
            var result = await _playerComparisonService.ComparePlayersActivityHoursAsync(player1, player2);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing activity hours for players {Player1} and {Player2}", player1, player2);
            return StatusCode(500, "An internal server error occurred while comparing player activity hours.");
        }
    }

}
