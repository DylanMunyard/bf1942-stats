using api.Servers.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Servers;

[ApiController]
[Route("stats/[controller]")]
public class RoundsController : ControllerBase
{
    private readonly RoundsService _roundsService;
    private readonly ILogger<RoundsController> _logger;

    public RoundsController(RoundsService roundsService, ILogger<RoundsController> logger)
    {
        _roundsService = roundsService;
        _logger = logger;
    }

    // Get all rounds with filtering and pagination support
    [HttpGet]
    public async Task<ActionResult<Players.Models.PagedResult<RoundWithPlayers>>> GetRounds(
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
        [FromQuery] DateTime? endTimeFrom = null,
        [FromQuery] DateTime? endTimeTo = null,
        [FromQuery] int? minDuration = null,
        [FromQuery] int? maxDuration = null,
        [FromQuery] int? minParticipants = null,
        [FromQuery] int? maxParticipants = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? gameId = null,
        [FromQuery] List<string>? playerNames = null,
        [FromQuery] bool includePlayers = true,
        [FromQuery] bool onlySpecifiedPlayers = false)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        // Valid sort fields for rounds
        var validSortFields = new[]
        {
            "RoundId", "ServerName", "MapName", "GameType", "StartTime", "EndTime",
            "DurationMinutes", "ParticipantCount", "IsActive"
        };

        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        if (!new[] { "asc", "desc" }.Contains(sortOrder.ToLower()))
            return BadRequest("Sort order must be 'asc' or 'desc'");

        // Validate filter parameters
        if (minDuration.HasValue && minDuration < 0)
            return BadRequest("Minimum duration cannot be negative");

        if (maxDuration.HasValue && maxDuration < 0)
            return BadRequest("Maximum duration cannot be negative");

        if (minDuration.HasValue && maxDuration.HasValue && minDuration > maxDuration)
            return BadRequest("Minimum duration cannot be greater than maximum duration");

        if (minParticipants.HasValue && minParticipants < 0)
            return BadRequest("Minimum participants cannot be negative");

        if (maxParticipants.HasValue && maxParticipants < 0)
            return BadRequest("Maximum participants cannot be negative");

        if (minParticipants.HasValue && maxParticipants.HasValue && minParticipants > maxParticipants)
            return BadRequest("Minimum participants cannot be greater than maximum participants");

        if (startTimeFrom.HasValue && startTimeTo.HasValue && startTimeFrom > startTimeTo)
            return BadRequest("StartTimeFrom cannot be greater than StartTimeTo");

        if (endTimeFrom.HasValue && endTimeTo.HasValue && endTimeFrom > endTimeTo)
            return BadRequest("EndTimeFrom cannot be greater than EndTimeTo");

        try
        {
            var filters = new RoundFilters
            {
                ServerName = serverName?.Trim(),
                ServerGuid = serverGuid,
                MapName = mapName,
                GameType = gameType,
                StartTimeFrom = startTimeFrom,
                StartTimeTo = startTimeTo,
                EndTimeFrom = endTimeFrom,
                EndTimeTo = endTimeTo,
                MinDuration = minDuration,
                MaxDuration = maxDuration,
                MinParticipants = minParticipants,
                MaxParticipants = maxParticipants,
                IsActive = isActive,
                GameId = gameId,
                PlayerNames = playerNames != null && playerNames.Any()
                    ? playerNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList()
                    : null
            };

            var result = await _roundsService.GetRounds(page, pageSize, sortBy, sortOrder, filters, includePlayers, onlySpecifiedPlayers);

            if (result.TotalItems == 0)
                return NotFound("No rounds found with the specified filters");

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving rounds with filters");
            return StatusCode(500, "An internal server error occurred while retrieving rounds");
        }
    }

    // Get round report by round ID
    [HttpGet("{roundId}/report")]
    public async Task<ActionResult<SessionRoundReport>> GetRoundReport(
        string roundId,
        [FromServices] Gamification.Services.ClickHouseGamificationService gamificationService)
    {
        if (string.IsNullOrWhiteSpace(roundId))
            return BadRequest("Round ID is required");

        try
        {
            var roundReport = await _roundsService.GetRoundReport(roundId, gamificationService);

            if (roundReport == null)
                return NotFound($"Round '{roundId}' not found");

            return Ok(roundReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving round report for round {RoundId}", roundId);
            return StatusCode(500, $"Error retrieving round report: {ex.Message}");
        }
    }

}
