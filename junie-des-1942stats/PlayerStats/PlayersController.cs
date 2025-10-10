using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Telemetry;
using System.Diagnostics;

namespace junie_des_1942stats.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly PlayerStatsService _playerStatsService;
    private readonly ServerStatisticsService _serverStatisticsService;
    private readonly PlayerComparisonService _playerComparisonService;
    private readonly PlayerRoundsReadService _playerRoundsService;
    private readonly ILogger<PlayersController> _logger;

    public PlayersController(PlayerStatsService playerStatsService, ServerStatisticsService serverStatisticsService, PlayerComparisonService playerComparisonService, PlayerRoundsReadService playerRoundsService, ILogger<PlayersController> logger)
    {
        _playerStatsService = playerStatsService;
        _serverStatisticsService = serverStatisticsService;
        _playerComparisonService = playerComparisonService;
        _playerRoundsService = playerRoundsService;
        _logger = logger;
    }

    // Get all players with basic info - enhanced with paging and sorting
    [HttpGet]
    public async Task<ActionResult<PagedResult<PlayerBasicInfo>>> GetAllPlayers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortBy = "IsActive",
        [FromQuery] string sortOrder = "desc",
        [FromQuery] string? playerName = null,
        [FromQuery] int? minPlayTime = null,
        [FromQuery] int? maxPlayTime = null,
        [FromQuery] DateTime? lastSeenFrom = null,
        [FromQuery] DateTime? lastSeenTo = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? serverName = null,
        [FromQuery] string? gameId = null,
        [FromQuery] string? mapName = null)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        // Valid sort fields
        var validSortFields = new[]
        {
            "PlayerName", "TotalPlayTimeMinutes", "LastSeen", "IsActive"
        };

        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        if (!new[] { "asc", "desc" }.Contains(sortOrder.ToLower()))
            return BadRequest("Sort order must be 'asc' or 'desc'");

        // Validate filter parameters
        if (minPlayTime.HasValue && minPlayTime < 0)
            return BadRequest("Minimum play time cannot be negative");

        if (maxPlayTime.HasValue && maxPlayTime < 0)
            return BadRequest("Maximum play time cannot be negative");

        if (minPlayTime.HasValue && maxPlayTime.HasValue && minPlayTime > maxPlayTime)
            return BadRequest("Minimum play time cannot be greater than maximum play time");

        if (lastSeenFrom.HasValue && lastSeenTo.HasValue && lastSeenFrom > lastSeenTo)
            return BadRequest("LastSeenFrom cannot be greater than LastSeenTo");

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
            return BadRequest("Player name cannot be empty");

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
            return BadRequest("Player name cannot be empty");

        if (string.IsNullOrWhiteSpace(serverGuid))
            return BadRequest("Server GUID cannot be empty");

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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query cannot be empty");

        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 100)
            return BadRequest("Page size must be between 1 and 100");

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
            return BadRequest("Both player1 and player2 must be provided.");

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
    public async Task<IActionResult> GetSimilarPlayers(string playerName, [FromQuery] int limit = 10, [FromQuery] string mode = "default")
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        if (limit < 1 || limit > 50)
            return BadRequest("Limit must be between 1 and 50");

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
            return BadRequest("Both player1 and player2 must be provided.");

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