using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.AspNetCore.Mvc;

namespace junie_des_1942stats.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly PlayerStatsService _playerStatsService;

    public PlayersController(PlayerStatsService playerStatsService)
    {
        _playerStatsService = playerStatsService;
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
                PlayerName = playerName,
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
        [FromQuery] int pageSize = 100)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
        
        if (page < 1)
            return BadRequest("Page number must be at least 1");
        
        if (pageSize < 1 || pageSize > 100)
            return BadRequest("Page size must be between 1 and 100");
        
        var result = await _playerStatsService.GetPlayerSessions(playerName, page, pageSize);
        
        if (result.TotalItems == 0)
            return NotFound($"No sessions found for player '{playerName}'");
        
        return Ok(result);
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

    // Get session round report with leaderboard
    [HttpGet("sessions/{sessionId}/round-report")]
    public async Task<ActionResult<SessionRoundReport>> GetSessionRoundReport(int sessionId)
    {
        if (sessionId <= 0)
            return BadRequest("Session ID must be positive");

        try
        {
            var roundReport = await _playerStatsService.GetSessionRoundReport(sessionId);
            
            if (roundReport == null)
                return NotFound($"Session {sessionId} not found");

            return Ok(roundReport);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving session round report: {ex.Message}");
        }
    }
}