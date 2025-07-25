using PlayerStatsModels = junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ServerStats;

[ApiController]
[Route("stats/[controller]")]
public class RoundsController : ControllerBase
{
    private readonly HistoricalRoundsService _roundsService;
    private readonly ILogger<RoundsController> _logger;

    public RoundsController(HistoricalRoundsService roundsService, ILogger<RoundsController> logger)
    {
        _roundsService = roundsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RoundListItem>>> GetAllRounds(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
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
        [FromQuery] string? gameId = null)
    {
        if (page < 1)
            return BadRequest("Page number must be at least 1");
        
        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        var validSortFields = new[]
        {
            "StartTime", "EndTime", "DurationMinutes", "ParticipantCount", 
            "ServerName", "MapName", "IsActive"
        };

        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        if (!new[] { "asc", "desc" }.Contains(sortOrder.ToLower()))
            return BadRequest("Sort order must be 'asc' or 'desc'");

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
                ServerGuid = serverGuid?.Trim(),
                MapName = mapName?.Trim(),
                GameType = gameType?.Trim(),
                StartTimeFrom = startTimeFrom,
                StartTimeTo = startTimeTo,
                EndTimeFrom = endTimeFrom,
                EndTimeTo = endTimeTo,
                MinDuration = minDuration,
                MaxDuration = maxDuration,
                MinParticipants = minParticipants,
                MaxParticipants = maxParticipants,
                IsActive = isActive,
                GameId = gameId?.Trim()
            };

            var result = await _roundsService.GetAllRounds(page, pageSize, sortBy, sortOrder, filters);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

}