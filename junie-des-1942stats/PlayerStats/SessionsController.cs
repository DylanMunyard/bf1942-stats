using junie_des_1942stats.PlayerStats.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly SessionsService _sessionsService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(SessionsService sessionsService, ILogger<SessionsController> logger)
    {
        _sessionsService = sessionsService;
        _logger = logger;
    }

    // Get all sessions with filtering and pagination support
    [HttpGet]
    public async Task<ActionResult<PagedResult<SessionListItem>>> GetSessions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string sortBy = "StartTime",
        [FromQuery] string sortOrder = "desc",
        [FromQuery] string? playerName = null,
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
        // Validate parameters
        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        // Valid sort fields for sessions
        var validSortFields = new[]
        {
            "SessionId", "PlayerName", "ServerName", "MapName", "GameType", "StartTime", "EndTime",
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
                PlayerName = playerName?.Trim(),
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

            var result = await _sessionsService.GetSessions(page, pageSize, sortBy, sortOrder, filters);

            if (result.TotalItems == 0)
                return NotFound("No sessions found with the specified filters");

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sessions with filters");
            return StatusCode(500, "An internal server error occurred while retrieving sessions");
        }
    }
}