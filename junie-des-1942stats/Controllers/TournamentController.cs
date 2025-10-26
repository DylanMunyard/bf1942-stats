using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using junie_des_1942stats.PlayerTracking;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class TournamentController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly ILogger<TournamentController> _logger;

    public TournamentController(
        PlayerTrackerDbContext context,
        ILogger<TournamentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all tournaments
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TournamentListResponse>>> GetTournaments()
    {
        try
        {
            var tournaments = await _context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.TournamentRounds)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TournamentListResponse
                {
                    Id = t.Id,
                    Name = t.Name,
                    Organizer = t.Organizer,
                    CreatedAt = t.CreatedAt,
                    RoundCount = t.TournamentRounds.Count
                })
                .ToListAsync();

            return Ok(tournaments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournaments");
            return StatusCode(500, new { message = "Error retrieving tournaments" });
        }
    }

    /// <summary>
    /// Get tournament by ID with full details including rounds and winners
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TournamentDetailResponse>> GetTournament(int id)
    {
        try
        {
            var tournament = await _context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.TournamentRounds)
                    .ThenInclude(tr => tr.Round)
                        .ThenInclude(r => r.Sessions)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Build tournament detail response with rounds and winners
            var rounds = new List<TournamentRoundResponse>();
            foreach (var tr in tournament.TournamentRounds.OrderBy(tr => tr.Round.StartTime))
            {
                var round = tr.Round;

                // Determine winning team
                string? winningTeam = null;
                if (round.Tickets1.HasValue && round.Tickets2.HasValue)
                {
                    winningTeam = round.Tickets1.Value > round.Tickets2.Value
                        ? round.Team1Label
                        : round.Team2Label;
                }

                // Get winning players (all players on the winning team)
                var winningPlayers = new List<string>();
                if (winningTeam != null)
                {
                    int winningTeamNumber = winningTeam == round.Team1Label ? 1 : 2;
                    winningPlayers = round.Sessions
                        .Where(s => s.CurrentTeam == winningTeamNumber)
                        .Select(s => s.PlayerName)
                        .Distinct()
                        .OrderBy(p => p)
                        .ToList();
                }

                rounds.Add(new TournamentRoundResponse
                {
                    RoundId = round.RoundId,
                    ServerGuid = round.ServerGuid,
                    ServerName = round.ServerName,
                    MapName = round.MapName,
                    StartTime = round.StartTime,
                    EndTime = round.EndTime,
                    WinningTeam = winningTeam,
                    WinningPlayers = winningPlayers,
                    Tickets1 = round.Tickets1,
                    Tickets2 = round.Tickets2,
                    Team1Label = round.Team1Label,
                    Team2Label = round.Team2Label
                });
            }

            // Determine overall winner (last round winners)
            TournamentWinnerResponse? overallWinner = null;
            var lastRound = rounds.OrderByDescending(r => r.StartTime).FirstOrDefault();
            if (lastRound != null && lastRound.WinningTeam != null)
            {
                overallWinner = new TournamentWinnerResponse
                {
                    Team = lastRound.WinningTeam,
                    Players = lastRound.WinningPlayers
                };
            }

            var response = new TournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                CreatedAt = tournament.CreatedAt,
                Rounds = rounds,
                OverallWinner = overallWinner
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error retrieving tournament" });
        }
    }

    /// <summary>
    /// Create a new tournament (authenticated users only)
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<TournamentDetailResponse>> CreateTournament([FromBody] CreateTournamentRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Tournament name is required" });

            if (string.IsNullOrWhiteSpace(request.Organizer))
                return BadRequest(new { message = "Organizer name is required" });

            if (request.RoundIds == null || request.RoundIds.Count == 0)
                return BadRequest(new { message = "At least one round is required" });

            // Verify organizer exists as a player
            var organizer = await _context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
            if (organizer == null)
                return BadRequest(new { message = $"Player '{request.Organizer}' not found" });

            // Verify all rounds exist
            var rounds = await _context.Rounds
                .Where(r => request.RoundIds.Contains(r.RoundId))
                .ToListAsync();

            if (rounds.Count != request.RoundIds.Count)
                return BadRequest(new { message = "One or more round IDs are invalid" });

            // Check if any rounds are already in another tournament
            var existingTournamentRounds = await _context.TournamentRounds
                .Where(tr => request.RoundIds.Contains(tr.RoundId))
                .ToListAsync();

            if (existingTournamentRounds.Any())
            {
                var conflictingRoundIds = string.Join(", ", existingTournamentRounds.Select(tr => tr.RoundId));
                return BadRequest(new { message = $"The following rounds are already in a tournament: {conflictingRoundIds}" });
            }

            // Create tournament
            var tournament = new Tournament
            {
                Name = request.Name,
                Organizer = request.Organizer,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = user.Id
            };

            _context.Tournaments.Add(tournament);
            await _context.SaveChangesAsync();

            // Add tournament rounds
            foreach (var roundId in request.RoundIds)
            {
                _context.TournamentRounds.Add(new TournamentRound
                {
                    TournamentId = tournament.Id,
                    RoundId = roundId
                });
            }

            await _context.SaveChangesAsync();

            // Return created tournament with full details
            return CreatedAtAction(
                nameof(GetTournament),
                new { id = tournament.Id },
                await GetTournamentDetailAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tournament");
            return StatusCode(500, new { message = "Error creating tournament" });
        }
    }

    /// <summary>
    /// Update a tournament (authenticated users only)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult<TournamentDetailResponse>> UpdateTournament(int id, [FromBody] UpdateTournamentRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var tournament = await _context.Tournaments
                .Include(t => t.TournamentRounds)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Validate request
            if (!string.IsNullOrWhiteSpace(request.Name))
                tournament.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Organizer))
            {
                var organizer = await _context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
                if (organizer == null)
                    return BadRequest(new { message = $"Player '{request.Organizer}' not found" });

                tournament.Organizer = request.Organizer;
            }

            // Update rounds if provided
            if (request.RoundIds != null && request.RoundIds.Count > 0)
            {
                // Verify all rounds exist
                var rounds = await _context.Rounds
                    .Where(r => request.RoundIds.Contains(r.RoundId))
                    .ToListAsync();

                if (rounds.Count != request.RoundIds.Count)
                    return BadRequest(new { message = "One or more round IDs are invalid" });

                // Check if any rounds are already in another tournament
                var existingTournamentRounds = await _context.TournamentRounds
                    .Where(tr => request.RoundIds.Contains(tr.RoundId) && tr.TournamentId != id)
                    .ToListAsync();

                if (existingTournamentRounds.Any())
                {
                    var conflictingRoundIds = string.Join(", ", existingTournamentRounds.Select(tr => tr.RoundId));
                    return BadRequest(new { message = $"The following rounds are already in a tournament: {conflictingRoundIds}" });
                }

                // Remove old rounds
                _context.TournamentRounds.RemoveRange(tournament.TournamentRounds);

                // Add new rounds
                foreach (var roundId in request.RoundIds)
                {
                    _context.TournamentRounds.Add(new TournamentRound
                    {
                        TournamentId = tournament.Id,
                        RoundId = roundId
                    });
                }
            }

            await _context.SaveChangesAsync();

            // Return updated tournament with full details
            return Ok(await GetTournamentDetailAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error updating tournament" });
        }
    }

    /// <summary>
    /// Delete a tournament (authenticated users only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteTournament(int id)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var tournament = await _context.Tournaments.FindAsync(id);
            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            _context.Tournaments.Remove(tournament);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error deleting tournament" });
        }
    }

    // Helper methods
    private async Task<User?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var id))
            return null;

        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    private async Task<TournamentDetailResponse> GetTournamentDetailAsync(int tournamentId)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.OrganizerPlayer)
            .Include(t => t.TournamentRounds)
                .ThenInclude(tr => tr.Round)
                    .ThenInclude(r => r.Sessions)
            .FirstAsync(t => t.Id == tournamentId);

        // Build tournament detail response with rounds and winners
        var rounds = new List<TournamentRoundResponse>();
        foreach (var tr in tournament.TournamentRounds.OrderBy(tr => tr.Round.StartTime))
        {
            var round = tr.Round;

            // Determine winning team
            string? winningTeam = null;
            if (round.Tickets1.HasValue && round.Tickets2.HasValue)
            {
                winningTeam = round.Tickets1.Value > round.Tickets2.Value
                    ? round.Team1Label
                    : round.Team2Label;
            }

            // Get winning players (all players on the winning team)
            var winningPlayers = new List<string>();
            if (winningTeam != null)
            {
                int winningTeamNumber = winningTeam == round.Team1Label ? 1 : 2;
                winningPlayers = round.Sessions
                    .Where(s => s.CurrentTeam == winningTeamNumber)
                    .Select(s => s.PlayerName)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();
            }

            rounds.Add(new TournamentRoundResponse
            {
                RoundId = round.RoundId,
                ServerGuid = round.ServerGuid,
                ServerName = round.ServerName,
                MapName = round.MapName,
                StartTime = round.StartTime,
                EndTime = round.EndTime,
                WinningTeam = winningTeam,
                WinningPlayers = winningPlayers,
                Tickets1 = round.Tickets1,
                Tickets2 = round.Tickets2,
                Team1Label = round.Team1Label,
                Team2Label = round.Team2Label
            });
        }

        // Determine overall winner (last round winners)
        TournamentWinnerResponse? overallWinner = null;
        var lastRound = rounds.OrderByDescending(r => r.StartTime).FirstOrDefault();
        if (lastRound != null && lastRound.WinningTeam != null)
        {
            overallWinner = new TournamentWinnerResponse
            {
                Team = lastRound.WinningTeam,
                Players = lastRound.WinningPlayers
            };
        }

        return new TournamentDetailResponse
        {
            Id = tournament.Id,
            Name = tournament.Name,
            Organizer = tournament.Organizer,
            CreatedAt = tournament.CreatedAt,
            Rounds = rounds,
            OverallWinner = overallWinner
        };
    }
}

// Request DTOs
public class CreateTournamentRequest
{
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public List<string> RoundIds { get; set; } = [];
}

public class UpdateTournamentRequest
{
    public string? Name { get; set; }
    public string? Organizer { get; set; }
    public List<string>? RoundIds { get; set; }
}

// Response DTOs
public class TournamentListResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int RoundCount { get; set; }
}

public class TournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<TournamentRoundResponse> Rounds { get; set; } = [];
    public TournamentWinnerResponse? OverallWinner { get; set; }
}

public class TournamentRoundResponse
{
    public string RoundId { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? WinningTeam { get; set; }
    public List<string> WinningPlayers { get; set; } = [];
    public int? Tickets1 { get; set; }
    public int? Tickets2 { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
}

public class TournamentWinnerResponse
{
    public string Team { get; set; } = "";
    public List<string> Players { get; set; } = [];
}
