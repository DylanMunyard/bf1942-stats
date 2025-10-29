using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using junie_des_1942stats.PlayerTracking;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/admin/tournaments")]
public class AdminTournamentController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly ILogger<AdminTournamentController> _logger;

    public AdminTournamentController(
        PlayerTrackerDbContext context,
        ILogger<AdminTournamentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get tournaments created by the current user
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<TournamentListResponse>>> GetTournaments()
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournaments = await _context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.TournamentRounds)
                .Include(t => t.Server)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TournamentListResponse
                {
                    Id = t.Id,
                    Name = t.Name,
                    Organizer = t.Organizer,
                    Game = t.Game,
                    CreatedAt = t.CreatedAt,
                    AnticipatedRoundCount = t.AnticipatedRoundCount,
                    RoundCount = t.TournamentRounds.Count,
                    HasHeroImage = t.HeroImage != null,
                    ServerGuid = t.ServerGuid,
                    ServerName = t.Server != null ? t.Server.Name : null
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
    [Authorize]
    public async Task<ActionResult<TournamentDetailResponse>> GetTournament(int id)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.Server)
                .Include(t => t.TournamentRounds)
                    .ThenInclude(tr => tr.Round)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });


            var rounds = tournament.TournamentRounds
                .OrderBy(tr => tr.Round.StartTime)
                .Select(tr => new TournamentRoundResponse
                {
                    RoundId = tr.Round.RoundId,
                    ServerGuid = tr.Round.ServerGuid,
                    ServerName = tr.Round.ServerName,
                    MapName = tr.Round.MapName,
                    StartTime = tr.Round.StartTime,
                    EndTime = tr.Round.EndTime,
                    Tickets1 = tr.Round.Tickets1,
                    Tickets2 = tr.Round.Tickets2,
                    Team1Label = tr.Round.Team1Label,
                    Team2Label = tr.Round.Team2Label
                })
                .ToList();


            var response = new TournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                Game = tournament.Game,
                CreatedAt = tournament.CreatedAt,
                AnticipatedRoundCount = tournament.AnticipatedRoundCount,
                Rounds = rounds,
                HeroImageBase64 = tournament.HeroImage != null ? Convert.ToBase64String(tournament.HeroImage) : null,
                HeroImageContentType = tournament.HeroImageContentType,
                ServerGuid = tournament.ServerGuid,
                ServerName = tournament.Server?.Name
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

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Tournament name is required" });

            if (string.IsNullOrWhiteSpace(request.Organizer))
                return BadRequest(new { message = "Organizer name is required" });

            if (string.IsNullOrWhiteSpace(request.Game))
                return BadRequest(new { message = "Game is required" });

            var allowedGames = new[] { "bf1942", "fh2", "bfvietnam" };
            if (!allowedGames.Contains(request.Game.ToLower()))
                return BadRequest(new { message = $"Invalid game. Allowed values: {string.Join(", ", allowedGames)}" });

            var organizer = await _context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
            if (organizer == null)
                return BadRequest(new { message = $"Player '{request.Organizer}' not found" });

            if (!string.IsNullOrWhiteSpace(request.ServerGuid))
            {
                var server = await _context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
                if (server == null)
                    return BadRequest(new { message = $"Server with GUID '{request.ServerGuid}' not found" });
            }

            if (request.RoundIds != null && request.RoundIds.Count > 0)
            {
                var rounds = await _context.Rounds
                    .Where(r => request.RoundIds.Contains(r.RoundId))
                    .ToListAsync();

                if (rounds.Count != request.RoundIds.Count)
                    return BadRequest(new { message = "One or more round IDs are invalid" });

                var existingTournamentRounds = await _context.TournamentRounds
                    .Where(tr => request.RoundIds.Contains(tr.RoundId))
                    .ToListAsync();

                if (existingTournamentRounds.Any())
                {
                    var conflictingRoundIds = string.Join(", ", existingTournamentRounds.Select(tr => tr.RoundId));
                    return BadRequest(new { message = $"The following rounds are already in a tournament: {conflictingRoundIds}" });
                }
            }

            var (heroImageData, imageError) = ValidateAndProcessImage(request.HeroImageBase64, request.HeroImageContentType);
            if (imageError != null)
                return BadRequest(new { message = imageError });

            var tournament = new Tournament
            {
                Name = request.Name,
                Organizer = request.Organizer,
                Game = request.Game.ToLower(),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = user.Id,
                CreatedByUserEmail = userEmail,
                AnticipatedRoundCount = request.AnticipatedRoundCount,
                HeroImage = heroImageData,
                HeroImageContentType = heroImageData != null ? request.HeroImageContentType : null,
                ServerGuid = !string.IsNullOrWhiteSpace(request.ServerGuid) ? request.ServerGuid : null
            };

            _context.Tournaments.Add(tournament);
            await _context.SaveChangesAsync();

            if (request.RoundIds != null && request.RoundIds.Count > 0)
            {
                foreach (var roundId in request.RoundIds)
                {
                    _context.TournamentRounds.Add(new TournamentRound
                    {
                        TournamentId = tournament.Id,
                        RoundId = roundId
                    });
                }

                await _context.SaveChangesAsync();
            }

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

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Include(t => t.TournamentRounds)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
                tournament.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Organizer))
            {
                var organizer = await _context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
                if (organizer == null)
                    return BadRequest(new { message = $"Player '{request.Organizer}' not found" });

                tournament.Organizer = request.Organizer;
            }

            if (!string.IsNullOrWhiteSpace(request.Game))
            {
                var allowedGames = new[] { "bf1942", "fh2", "bfvietnam" };
                if (!allowedGames.Contains(request.Game.ToLower()))
                    return BadRequest(new { message = $"Invalid game. Allowed values: {string.Join(", ", allowedGames)}" });

                tournament.Game = request.Game.ToLower();
            }

            if (request.AnticipatedRoundCount.HasValue)
                tournament.AnticipatedRoundCount = request.AnticipatedRoundCount;

            if (request.ServerGuid != null)
            {
                if (!string.IsNullOrWhiteSpace(request.ServerGuid))
                {
                    var server = await _context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
                    if (server == null)
                        return BadRequest(new { message = $"Server with GUID '{request.ServerGuid}' not found" });
                    
                    tournament.ServerGuid = request.ServerGuid;
                }
                else
                {
                    tournament.ServerGuid = null;
                }
            }

            if (request.HeroImageBase64 != null)
            {
                var (heroImageData, imageError) = ValidateAndProcessImage(request.HeroImageBase64, request.HeroImageContentType);
                if (imageError != null)
                    return BadRequest(new { message = imageError });

                tournament.HeroImage = heroImageData;
                tournament.HeroImageContentType = heroImageData != null ? request.HeroImageContentType : null;
            }

            if (request.RoundIds != null && request.RoundIds.Count > 0)
            {
                var rounds = await _context.Rounds
                    .Where(r => request.RoundIds.Contains(r.RoundId))
                    .ToListAsync();

                if (rounds.Count != request.RoundIds.Count)
                    return BadRequest(new { message = "One or more round IDs are invalid" });

                var existingTournamentRounds = await _context.TournamentRounds
                    .Where(tr => request.RoundIds.Contains(tr.RoundId) && tr.TournamentId != id)
                    .ToListAsync();

                if (existingTournamentRounds.Any())
                {
                    var conflictingRoundIds = string.Join(", ", existingTournamentRounds.Select(tr => tr.RoundId));
                    return BadRequest(new { message = $"The following rounds are already in a tournament: {conflictingRoundIds}" });
                }

                _context.TournamentRounds.RemoveRange(tournament.TournamentRounds);

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

            return Ok(await GetTournamentDetailAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error updating tournament" });
        }
    }

    /// <summary>
    /// Add a round to an existing tournament (authenticated users only)
    /// </summary>
    [HttpPost("{id}/rounds")]
    [Authorize]
    public async Task<ActionResult<TournamentDetailResponse>> AddRoundToTournament(int id, [FromBody] AddRoundRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Include(t => t.TournamentRounds)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (string.IsNullOrWhiteSpace(request.RoundId))
                return BadRequest(new { message = "Round ID is required" });

            var round = await _context.Rounds.FirstOrDefaultAsync(r => r.RoundId == request.RoundId);
            if (round == null)
                return BadRequest(new { message = $"Round '{request.RoundId}' not found" });

            var existingTournamentRound = await _context.TournamentRounds
                .FirstOrDefaultAsync(tr => tr.RoundId == request.RoundId);

            if (existingTournamentRound != null)
                return BadRequest(new { message = $"Round '{request.RoundId}' is already in a tournament" });

            _context.TournamentRounds.Add(new TournamentRound
            {
                TournamentId = tournament.Id,
                RoundId = request.RoundId
            });

            await _context.SaveChangesAsync();

            return Ok(await GetTournamentDetailAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding round to tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error adding round to tournament" });
        }
    }

    /// <summary>
    /// Delete a round from a tournament (authenticated users only)
    /// </summary>
    [HttpDelete("{id}/rounds/{roundId}")]
    [Authorize]
    public async Task<ActionResult<TournamentDetailResponse>> DeleteRoundFromTournament(int id, string roundId)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Include(t => t.TournamentRounds)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var tournamentRound = await _context.TournamentRounds
                .FirstOrDefaultAsync(tr => tr.TournamentId == id && tr.RoundId == roundId);

            if (tournamentRound == null)
                return NotFound(new { message = $"Round '{roundId}' not found in this tournament" });

            _context.TournamentRounds.Remove(tournamentRound);
            await _context.SaveChangesAsync();

            return Ok(await GetTournamentDetailAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting round from tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error deleting round from tournament" });
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

            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

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

    /// <summary>
    /// Get tournament hero image
    /// </summary>
    [HttpGet("{id}/image")]
    [Authorize]
    public async Task<IActionResult> GetTournamentImage(int id)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Where(t => t.Id == id && t.CreatedByUserEmail == userEmail)
                .Select(t => new { t.HeroImage, t.HeroImageContentType })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (tournament.HeroImage == null)
                return NotFound(new { message = "Tournament has no hero image" });

            return File(tournament.HeroImage, tournament.HeroImageContentType ?? "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournament image {TournamentId}", id);
            return StatusCode(500, new { message = "Error retrieving tournament image" });
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

    private (byte[]? imageData, string? error) ValidateAndProcessImage(string? base64Image, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(base64Image))
            return (null, null);

        try
        {
            var imageBytes = Convert.FromBase64String(base64Image);

            const int maxSizeBytes = 4 * 1024 * 1024;
            if (imageBytes.Length > maxSizeBytes)
                return (null, $"Image size exceeds 4MB limit. Current size: {imageBytes.Length / 1024.0 / 1024.0:F2}MB");

            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
            if (!string.IsNullOrWhiteSpace(contentType) && !allowedTypes.Contains(contentType.ToLower()))
                return (null, $"Invalid image type. Allowed types: {string.Join(", ", allowedTypes)}");

            return (imageBytes, null);
        }
        catch (FormatException)
        {
            return (null, "Invalid base64 image format");
        }
    }

    private async Task<TournamentDetailResponse> GetTournamentDetailAsync(int tournamentId)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.OrganizerPlayer)
            .Include(t => t.Server)
            .Include(t => t.TournamentRounds)
                .ThenInclude(tr => tr.Round)
            .FirstAsync(t => t.Id == tournamentId);


        var rounds = tournament.TournamentRounds
            .OrderBy(tr => tr.Round.StartTime)
            .Select(tr => new TournamentRoundResponse
            {
                RoundId = tr.Round.RoundId,
                ServerGuid = tr.Round.ServerGuid,
                ServerName = tr.Round.ServerName,
                MapName = tr.Round.MapName,
                StartTime = tr.Round.StartTime,
                EndTime = tr.Round.EndTime,
                Tickets1 = tr.Round.Tickets1,
                Tickets2 = tr.Round.Tickets2,
                Team1Label = tr.Round.Team1Label,
                Team2Label = tr.Round.Team2Label
            })
            .ToList();


        return new TournamentDetailResponse
        {
            Id = tournament.Id,
            Name = tournament.Name,
            Organizer = tournament.Organizer,
            Game = tournament.Game,
            CreatedAt = tournament.CreatedAt,
            AnticipatedRoundCount = tournament.AnticipatedRoundCount,
            Rounds = rounds,
            HeroImageBase64 = tournament.HeroImage != null ? Convert.ToBase64String(tournament.HeroImage) : null,
            HeroImageContentType = tournament.HeroImageContentType,
            ServerGuid = tournament.ServerGuid,
            ServerName = tournament.Server?.Name
        };
    }
}

// Request DTOs
public class CreateTournamentRequest
{
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public int? AnticipatedRoundCount { get; set; }
    public List<string>? RoundIds { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }
}

public class UpdateTournamentRequest
{
    public string? Name { get; set; }
    public string? Organizer { get; set; }
    public string? Game { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public List<string>? RoundIds { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }
}

public class AddRoundRequest
{
    public string RoundId { get; set; } = "";
}

// Response DTOs
public class TournamentListResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public int RoundCount { get; set; }
    public bool HasHeroImage { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
}

public class TournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public List<TournamentRoundResponse> Rounds { get; set; } = [];
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
}

public class TournamentRoundResponse
{
    public string RoundId { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? Tickets1 { get; set; }
    public int? Tickets2 { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
}


