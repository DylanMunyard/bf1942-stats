using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ServerStats;
using junie_des_1942stats.ServerStats.Models;
using junie_des_1942stats.PlayerStats.Models;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentsController : ControllerBase
{
    private readonly RoundsService _roundsService;
    private readonly ILogger<TournamentsController> _logger;

    public TournamentsController(RoundsService roundsService, ILogger<TournamentsController> logger)
    {
        _roundsService = roundsService;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated list of tournaments with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<TournamentWithRounds>>> GetTournaments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "startTime",
        [FromQuery] string sortOrder = "desc",
        [FromQuery] string? serverName = null,
        [FromQuery] string? serverGuid = null,
        [FromQuery] string? mapName = null,
        [FromQuery] string? gameType = null,
        [FromQuery] string? tournamentType = null,
        [FromQuery] DateTime? startTimeFrom = null,
        [FromQuery] DateTime? startTimeTo = null,
        [FromQuery] DateTime? endTimeFrom = null,
        [FromQuery] DateTime? endTimeTo = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int? minRounds = null,
        [FromQuery] int? maxRounds = null,
        [FromQuery] bool includeRounds = false)
    {
        try
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100; // Limit max page size

            var filters = new TournamentFilters
            {
                ServerName = serverName,
                ServerGuid = serverGuid,
                MapName = mapName,
                GameType = gameType,
                TournamentType = tournamentType,
                StartTimeFrom = startTimeFrom,
                StartTimeTo = startTimeTo,
                EndTimeFrom = endTimeFrom,
                EndTimeTo = endTimeTo,
                IsActive = isActive,
                MinRounds = minRounds,
                MaxRounds = maxRounds
            };

            var result = await _roundsService.GetTournaments(page, pageSize, sortBy, sortOrder, filters, includeRounds);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournaments");
            return StatusCode(500, "An error occurred while retrieving tournaments");
        }
    }

    /// <summary>
    /// Get a specific tournament by ID with its rounds
    /// </summary>
    [HttpGet("{tournamentId}")]
    public async Task<ActionResult<TournamentWithRounds>> GetTournament(
        string tournamentId,
        [FromQuery] bool includeRounds = true)
    {
        try
        {
            var tournament = await _roundsService.GetTournament(tournamentId, includeRounds);
            
            if (tournament == null)
            {
                return NotFound($"Tournament with ID '{tournamentId}' not found");
            }

            return Ok(tournament);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournament {TournamentId}", tournamentId);
            return StatusCode(500, "An error occurred while retrieving the tournament");
        }
    }

    /// <summary>
    /// Get rounds that belong to a specific tournament
    /// </summary>
    [HttpGet("{tournamentId}/rounds")]
    public async Task<ActionResult<PagedResult<RoundWithPlayers>>> GetTournamentRounds(
        string tournamentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "startTime",
        [FromQuery] string sortOrder = "asc",
        [FromQuery] bool includePlayers = false,
        [FromQuery] bool onlySpecifiedPlayers = false,
        [FromQuery] List<string>? playerNames = null)
    {
        try
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var filters = new RoundFilters
            {
                TournamentId = tournamentId,
                PlayerNames = playerNames
            };

            var result = await _roundsService.GetRounds(page, pageSize, sortBy, sortOrder, filters, includePlayers, onlySpecifiedPlayers);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rounds for tournament {TournamentId}", tournamentId);
            return StatusCode(500, "An error occurred while retrieving tournament rounds");
        }
    }

    /// <summary>
    /// Get recent tournaments for a specific server
    /// </summary>
    [HttpGet("server/{serverGuid}/recent")]
    public async Task<ActionResult<PagedResult<TournamentWithRounds>>> GetRecentServerTournaments(
        string serverGuid,
        [FromQuery] int limit = 10,
        [FromQuery] bool includeRounds = false)
    {
        try
        {
            var filters = new TournamentFilters
            {
                ServerGuid = serverGuid
            };

            var result = await _roundsService.GetTournaments(1, limit, "startTime", "desc", filters, includeRounds);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent tournaments for server {ServerGuid}", serverGuid);
            return StatusCode(500, "An error occurred while retrieving recent tournaments");
        }
    }

    /// <summary>
    /// Get active tournaments across all servers
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<PagedResult<TournamentWithRounds>>> GetActiveTournaments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeRounds = true)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var filters = new TournamentFilters
            {
                IsActive = true
            };

            var result = await _roundsService.GetTournaments(page, pageSize, "startTime", "desc", filters, includeRounds);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active tournaments");
            return StatusCode(500, "An error occurred while retrieving active tournaments");
        }
    }
}