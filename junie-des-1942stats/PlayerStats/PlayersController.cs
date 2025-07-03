using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly PlayerStatsService _playerStatsService;
    private readonly ServerStatisticsService _serverStatisticsService;
    private readonly PlayerComparisonService _playerComparisonService;
    private readonly PlayerRoundsService _playerRoundsService;
    private readonly ILogger<PlayersController> _logger;

    public PlayersController(PlayerStatsService playerStatsService, ServerStatisticsService serverStatisticsService, PlayerComparisonService playerComparisonService, PlayerRoundsService playerRoundsService, ILogger<PlayersController> logger)
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
        
        if (stats.TotalSessions == 0)
            return NotFound($"Player '{playerName}' not found");
            
        return Ok(stats);
    }

    // Get all sessions for a player with pagination support
    [HttpGet("{playerName}/sessions")]
    public async Task<ActionResult<PagedResult<SessionListItem>>> GetPlayerSessions(
        string playerName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string sortBy = "StartTime",
        [FromQuery] string sortOrder = "desc",
        [FromQuery] string? serverName = null,
        [FromQuery] string? serverGuid = null,
        [FromQuery] string? mapName = null,
        [FromQuery] string? gameType = null,
        [FromQuery] DateTime? startTimeFrom = null,
        [FromQuery] DateTime? startTimeTo = null,
        [FromQuery] DateTime? lastSeenFrom = null,
        [FromQuery] DateTime? lastSeenTo = null,
        [FromQuery] int? minPlayTime = null,
        [FromQuery] int? maxPlayTime = null,
        [FromQuery] int? minScore = null,
        [FromQuery] int? maxScore = null,
        [FromQuery] int? minKills = null,
        [FromQuery] int? maxKills = null,
        [FromQuery] int? minDeaths = null,
        [FromQuery] int? maxDeaths = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? gameId = null)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
        
        if (page < 1)
            return BadRequest("Page number must be at least 1");
        
        if (pageSize < 1 || pageSize > 100)
            return BadRequest("Page size must be between 1 and 100");

        // Valid sort fields for sessions
        var validSortFields = new[]
        {
            "SessionId", "ServerName", "MapName", "GameType", "StartTime", "EndTime", 
            "DurationMinutes", "Score", "Kills", "Deaths", "IsActive"
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

        if (minScore.HasValue && minScore < 0)
            return BadRequest("Minimum score cannot be negative");
        
        if (maxScore.HasValue && maxScore < 0)
            return BadRequest("Maximum score cannot be negative");
        
        if (minScore.HasValue && maxScore.HasValue && minScore > maxScore)
            return BadRequest("Minimum score cannot be greater than maximum score");

        if (minKills.HasValue && minKills < 0)
            return BadRequest("Minimum kills cannot be negative");
        
        if (maxKills.HasValue && maxKills < 0)
            return BadRequest("Maximum kills cannot be negative");
        
        if (minKills.HasValue && maxKills.HasValue && minKills > maxKills)
            return BadRequest("Minimum kills cannot be greater than maximum kills");

        if (minDeaths.HasValue && minDeaths < 0)
            return BadRequest("Minimum deaths cannot be negative");
        
        if (maxDeaths.HasValue && maxDeaths < 0)
            return BadRequest("Maximum deaths cannot be negative");
        
        if (minDeaths.HasValue && maxDeaths.HasValue && minDeaths > maxDeaths)
            return BadRequest("Minimum deaths cannot be greater than maximum deaths");
        
        if (startTimeFrom.HasValue && startTimeTo.HasValue && startTimeFrom > startTimeTo)
            return BadRequest("StartTimeFrom cannot be greater than StartTimeTo");
        
        if (lastSeenFrom.HasValue && lastSeenTo.HasValue && lastSeenFrom > lastSeenTo)
            return BadRequest("LastSeenFrom cannot be greater than LastSeenTo");

        try
        {
            var filters = new PlayerFilters
            {
                ServerName = serverName,
                ServerGuid = serverGuid,
                MapName = mapName,
                GameType = gameType,
                StartTimeFrom = startTimeFrom,
                StartTimeTo = startTimeTo,
                LastSeenFrom = lastSeenFrom,
                LastSeenTo = lastSeenTo,
                MinPlayTime = minPlayTime,
                MaxPlayTime = maxPlayTime,
                MinScore = minScore,
                MaxScore = maxScore,
                MinKills = minKills,
                MaxKills = maxKills,
                MinDeaths = minDeaths,
                MaxDeaths = maxDeaths,
                IsActive = isActive,
                GameId = gameId
            };

            var result = await _playerStatsService.GetPlayerSessions(playerName, page, pageSize, sortBy, sortOrder, filters);
            
            if (result.TotalItems == 0)
                return NotFound($"No sessions found for player '{playerName}' with the specified filters");
            
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
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
    public async Task<IActionResult> GetSimilarPlayers(string playerName, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        if (limit < 1 || limit > 50)
            return BadRequest("Limit must be between 1 and 50");

        try
        {
            var result = await _playerComparisonService.FindSimilarPlayersAsync(playerName, limit);
            
            if (result.TargetPlayerStats == null)
                return NotFound($"Player '{playerName}' not found or has insufficient data");
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar players for {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while finding similar players.");
        }
    }

    // NEW: Fast aggregated player statistics using ClickHouse player_rounds table
    // This demonstrates the performance improvements from the pre-aggregated round data
    [HttpGet("fast-stats")]
    public async Task<IActionResult> GetFastPlayerStats(
        [FromQuery] string? playerName = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        try
        {
            // Default to last 6 months if no date range specified
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = DateTime.UtcNow.AddMonths(-6);
            }

            var result = await _playerRoundsService.GetPlayerStatsAsync(playerName, fromDate, toDate);
            
            // Return raw TSV data with proper content type for demonstration
            // In production, you'd probably parse this into a proper model
            return Content(result, "text/tab-separated-values");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving fast player stats for player: {PlayerName}", playerName);
            return StatusCode(500, $"An error occurred while retrieving player statistics: {ex.Message}");
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