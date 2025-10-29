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
                .Include(t => t.Server)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var tournamentIds = tournaments.Select(t => t.Id).ToList();

            // Batch load match counts
            var matchCounts = await _context.TournamentMatches
                .Where(tm => tournamentIds.Contains(tm.TournamentId))
                .GroupBy(tm => tm.TournamentId)
                .Select(g => new { TournamentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TournamentId, x => x.Count);

            // Batch load team counts
            var teamCounts = await _context.TournamentTeams
                .Where(tt => tournamentIds.Contains(tt.TournamentId))
                .GroupBy(tt => tt.TournamentId)
                .Select(g => new { TournamentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TournamentId, x => x.Count);

            var response = tournaments.Select(t => new TournamentListResponse
            {
                Id = t.Id,
                Name = t.Name,
                Organizer = t.Organizer,
                Game = t.Game,
                CreatedAt = t.CreatedAt,
                AnticipatedRoundCount = t.AnticipatedRoundCount,
                MatchCount = matchCounts.GetValueOrDefault(t.Id, 0),
                TeamCount = teamCounts.GetValueOrDefault(t.Id, 0),
                HasHeroImage = t.HeroImage != null,
                ServerGuid = t.ServerGuid,
                ServerName = t.Server?.Name
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournaments");
            return StatusCode(500, new { message = "Error retrieving tournaments" });
        }
    }

    /// <summary>
    /// Get tournament by ID with full details including teams and matches
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
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Load teams and their players separately to avoid cartesian product
            var teams = await _context.TournamentTeams
                .Include(tt => tt.TeamPlayers)
                .Where(tt => tt.TournamentId == id)
                .Select(tt => new TournamentTeamResponse
                {
                    Id = tt.Id,
                    Name = tt.Name,
                    CreatedAt = tt.CreatedAt,
                    Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                    {
                        PlayerName = ttp.PlayerName
                    }).ToList()
                })
                .ToListAsync();

            // Load matches with team names (avoid loading full team objects)
            var matches = await _context.TournamentMatches
                .Where(tm => tm.TournamentId == id)
                .Select(tm => new
                {
                    tm.Id,
                    tm.ScheduledDate,
                    tm.MapName,
                    Team1Name = tm.Team1.Name,
                    Team2Name = tm.Team2.Name,
                    tm.ServerGuid,
                    tm.ServerName,
                    tm.RoundId,
                    tm.CreatedAt,
                    Round = tm.Round != null ? new TournamentRoundResponse
                    {
                        RoundId = tm.Round.RoundId,
                        ServerGuid = tm.Round.ServerGuid,
                        ServerName = tm.Round.ServerName,
                        MapName = tm.Round.MapName,
                        StartTime = tm.Round.StartTime,
                        EndTime = tm.Round.EndTime,
                        Tickets1 = tm.Round.Tickets1,
                        Tickets2 = tm.Round.Tickets2,
                        Team1Label = tm.Round.Team1Label,
                        Team2Label = tm.Round.Team2Label
                    } : null
                })
                .OrderBy(tm => tm.ScheduledDate)
                .ToListAsync();

            var matchResponses = matches.Select(m => new TournamentMatchResponse
            {
                Id = m.Id,
                ScheduledDate = m.ScheduledDate,
                MapName = m.MapName,
                Team1Name = m.Team1Name,
                Team2Name = m.Team2Name,
                ServerGuid = m.ServerGuid,
                ServerName = m.ServerName,
                RoundId = m.RoundId,
                CreatedAt = m.CreatedAt,
                Round = m.Round
            }).ToList();

            var response = new TournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                Game = tournament.Game,
                CreatedAt = tournament.CreatedAt,
                AnticipatedRoundCount = tournament.AnticipatedRoundCount,
                Teams = teams,
                Matches = matchResponses,
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

            return CreatedAtAction(
                nameof(GetTournament),
                new { id = tournament.Id },
                await GetTournamentDetailOptimizedAsync(tournament.Id));
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

            await _context.SaveChangesAsync();

            return Ok(await GetTournamentDetailOptimizedAsync(tournament.Id));
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

    private async Task<TournamentDetailResponse> GetTournamentDetailOptimizedAsync(int tournamentId)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.OrganizerPlayer)
            .Include(t => t.Server)
            .FirstAsync(t => t.Id == tournamentId);

        var teams = await _context.TournamentTeams
            .Include(tt => tt.TeamPlayers)
            .Where(tt => tt.TournamentId == tournamentId)
            .Select(tt => new TournamentTeamResponse
            {
                Id = tt.Id,
                Name = tt.Name,
                CreatedAt = tt.CreatedAt,
                Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                {
                    PlayerName = ttp.PlayerName
                }).ToList()
            })
            .ToListAsync();

        var matches = await _context.TournamentMatches
            .Where(tm => tm.TournamentId == tournamentId)
            .Select(tm => new TournamentMatchResponse
            {
                Id = tm.Id,
                ScheduledDate = tm.ScheduledDate,
                MapName = tm.MapName,
                Team1Name = tm.Team1.Name,
                Team2Name = tm.Team2.Name,
                ServerGuid = tm.ServerGuid,
                ServerName = tm.ServerName,
                RoundId = tm.RoundId,
                CreatedAt = tm.CreatedAt,
                Round = tm.Round != null ? new TournamentRoundResponse
                {
                    RoundId = tm.Round.RoundId,
                    ServerGuid = tm.Round.ServerGuid,
                    ServerName = tm.Round.ServerName,
                    MapName = tm.Round.MapName,
                    StartTime = tm.Round.StartTime,
                    EndTime = tm.Round.EndTime,
                    Tickets1 = tm.Round.Tickets1,
                    Tickets2 = tm.Round.Tickets2,
                    Team1Label = tm.Round.Team1Label,
                    Team2Label = tm.Round.Team2Label
                } : null
            })
            .OrderBy(tm => tm.ScheduledDate)
            .ToListAsync();

        return new TournamentDetailResponse
        {
            Id = tournament.Id,
            Name = tournament.Name,
            Organizer = tournament.Organizer,
            Game = tournament.Game,
            CreatedAt = tournament.CreatedAt,
            AnticipatedRoundCount = tournament.AnticipatedRoundCount,
            Teams = teams,
            Matches = matches,
            HeroImageBase64 = tournament.HeroImage != null ? Convert.ToBase64String(tournament.HeroImage) : null,
            HeroImageContentType = tournament.HeroImageContentType,
            ServerGuid = tournament.ServerGuid,
            ServerName = tournament.Server?.Name
        };
    }

    // ===== TEAM MANAGEMENT ENDPOINTS =====

    /// <summary>
    /// Create a new team for a tournament
    /// </summary>
    [HttpPost("{tournamentId}/teams")]
    [Authorize]
    public async Task<ActionResult<TournamentTeamResponse>> CreateTeam(int tournamentId, [FromBody] CreateTournamentTeamRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Team name is required" });

            // Check if team name already exists in this tournament
            var existingTeam = await _context.TournamentTeams
                .Where(tt => tt.TournamentId == tournamentId && tt.Name == request.Name)
                .FirstOrDefaultAsync();

            if (existingTeam != null)
                return BadRequest(new { message = $"Team '{request.Name}' already exists in this tournament" });

            var team = new TournamentTeam
            {
                TournamentId = tournamentId,
                Name = request.Name,
                CreatedAt = DateTime.UtcNow
            };

            _context.TournamentTeams.Add(team);
            await _context.SaveChangesAsync();

            var response = new TournamentTeamResponse
            {
                Id = team.Id,
                Name = team.Name,
                CreatedAt = team.CreatedAt,
                Players = []
            };

            return CreatedAtAction(
                nameof(GetTeam),
                new { tournamentId = tournamentId, teamId = team.Id },
                response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating team for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error creating team" });
        }
    }

    /// <summary>
    /// Get a specific team by ID
    /// </summary>
    [HttpGet("{tournamentId}/teams/{teamId}")]
    [Authorize]
    public async Task<ActionResult<TournamentTeamResponse>> GetTeam(int tournamentId, int teamId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var team = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .Select(tt => new TournamentTeamResponse
                {
                    Id = tt.Id,
                    Name = tt.Name,
                    CreatedAt = tt.CreatedAt,
                    Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                    {
                        PlayerName = ttp.PlayerName
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (team == null)
                return NotFound(new { message = "Team not found" });

            return Ok(team);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error retrieving team" });
        }
    }

    /// <summary>
    /// Update a team
    /// </summary>
    [HttpPut("{tournamentId}/teams/{teamId}")]
    [Authorize]
    public async Task<ActionResult<TournamentTeamResponse>> UpdateTeam(int tournamentId, int teamId, [FromBody] UpdateTournamentTeamRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var team = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (team == null)
                return NotFound(new { message = "Team not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                // Check if new name conflicts with existing team
                var existingTeam = await _context.TournamentTeams
                    .Where(tt => tt.TournamentId == tournamentId && tt.Name == request.Name && tt.Id != teamId)
                    .FirstOrDefaultAsync();

                if (existingTeam != null)
                    return BadRequest(new { message = $"Team '{request.Name}' already exists in this tournament" });

                team.Name = request.Name;
            }

            await _context.SaveChangesAsync();

            // Return updated team with players
            var response = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId)
                .Select(tt => new TournamentTeamResponse
                {
                    Id = tt.Id,
                    Name = tt.Name,
                    CreatedAt = tt.CreatedAt,
                    Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                    {
                        PlayerName = ttp.PlayerName
                    }).ToList()
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error updating team" });
        }
    }

    /// <summary>
    /// Delete a team
    /// </summary>
    [HttpDelete("{tournamentId}/teams/{teamId}")]
    [Authorize]
    public async Task<IActionResult> DeleteTeam(int tournamentId, int teamId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var team = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (team == null)
                return NotFound(new { message = "Team not found" });

            var matchesUsingTeam = await _context.TournamentMatches
                .Where(tm => tm.Team1Id == teamId || tm.Team2Id == teamId)
                .CountAsync();

            if (matchesUsingTeam > 0)
                return BadRequest(new { message = "Cannot delete team that is used in matches" });

            _context.TournamentTeams.Remove(team);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error deleting team" });
        }
    }

    /// <summary>
    /// Add a player to a team
    /// </summary>
    [HttpPost("{tournamentId}/teams/{teamId}/players")]
    [Authorize]
    public async Task<ActionResult<TournamentTeamResponse>> AddPlayerToTeam(int tournamentId, int teamId, [FromBody] AddPlayerToTeamRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var teamExists = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .AnyAsync();

            if (!teamExists)
                return NotFound(new { message = "Team not found" });

            if (string.IsNullOrWhiteSpace(request.PlayerName))
                return BadRequest(new { message = "Player name is required" });

            var player = await _context.Players.FirstOrDefaultAsync(p => p.Name == request.PlayerName);
            if (player == null)
                return BadRequest(new { message = $"Player '{request.PlayerName}' not found" });

            var existingTeamPlayer = await _context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == teamId && ttp.PlayerName == request.PlayerName)
                .FirstOrDefaultAsync();

            if (existingTeamPlayer != null)
                return BadRequest(new { message = $"Player '{request.PlayerName}' is already in this team" });

            var playerInOtherTeam = await _context.TournamentTeamPlayers
                .Where(ttp => ttp.PlayerName == request.PlayerName && ttp.TournamentTeam.TournamentId == tournamentId)
                .AnyAsync();

            if (playerInOtherTeam)
                return BadRequest(new { message = $"Player '{request.PlayerName}' is already in another team in this tournament" });

            var teamPlayer = new TournamentTeamPlayer
            {
                TournamentTeamId = teamId,
                PlayerName = request.PlayerName
            };

            _context.TournamentTeamPlayers.Add(teamPlayer);
            await _context.SaveChangesAsync();

            var response = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId)
                .Select(tt => new TournamentTeamResponse
                {
                    Id = tt.Id,
                    Name = tt.Name,
                    CreatedAt = tt.CreatedAt,
                    Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                    {
                        PlayerName = ttp.PlayerName
                    }).ToList()
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding player to team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error adding player to team" });
        }
    }

    /// <summary>
    /// Remove a player from a team
    /// </summary>
    [HttpDelete("{tournamentId}/teams/{teamId}/players/{playerName}")]
    [Authorize]
    public async Task<ActionResult<TournamentTeamResponse>> RemovePlayerFromTeam(int tournamentId, int teamId, string playerName)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var teamExists = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .AnyAsync();

            if (!teamExists)
                return NotFound(new { message = "Team not found" });

            var teamPlayer = await _context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == teamId && ttp.PlayerName == playerName)
                .FirstOrDefaultAsync();

            if (teamPlayer == null)
                return NotFound(new { message = $"Player '{playerName}' not found in this team" });

            _context.TournamentTeamPlayers.Remove(teamPlayer);
            await _context.SaveChangesAsync();

            var response = await _context.TournamentTeams
                .Where(tt => tt.Id == teamId)
                .Select(tt => new TournamentTeamResponse
                {
                    Id = tt.Id,
                    Name = tt.Name,
                    CreatedAt = tt.CreatedAt,
                    Players = tt.TeamPlayers.Select(ttp => new TournamentTeamPlayerResponse
                    {
                        PlayerName = ttp.PlayerName
                    }).ToList()
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player from team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error removing player from team" });
        }
    }

    // ===== MATCH MANAGEMENT ENDPOINTS =====

    /// <summary>
    /// Create a new match for a tournament
    /// </summary>
    [HttpPost("{tournamentId}/matches")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResponse>> CreateMatch(int tournamentId, [FromBody] CreateTournamentMatchRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (request.Team1Id <= 0 || request.Team2Id <= 0)
                return BadRequest(new { message = "Both Team1Id and Team2Id are required" });

            if (request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team1Id and Team2Id cannot be the same" });

            if (string.IsNullOrWhiteSpace(request.MapName))
                return BadRequest(new { message = "Map name is required" });

            var teamIds = new[] { request.Team1Id, request.Team2Id };
            var teams = await _context.TournamentTeams
                .Where(tt => teamIds.Contains(tt.Id) && tt.TournamentId == tournamentId)
                .Select(tt => new { tt.Id, tt.Name })
                .ToListAsync();

            if (teams.Count != 2)
            {
                var foundTeamIds = teams.Select(t => t.Id).ToList();
                var missingTeamIds = teamIds.Except(foundTeamIds).ToList();
                return BadRequest(new { message = $"Teams with IDs {string.Join(", ", missingTeamIds)} not found in this tournament" });
            }

            // Validate server if provided
            if (!string.IsNullOrWhiteSpace(request.ServerGuid))
            {
                var serverExists = await _context.Servers.AnyAsync(s => s.Guid == request.ServerGuid);
                if (!serverExists)
                    return BadRequest(new { message = $"Server with GUID '{request.ServerGuid}' not found" });
            }

            var match = new TournamentMatch
            {
                TournamentId = tournamentId,
                ScheduledDate = request.ScheduledDate,
                Team1Id = request.Team1Id,
                Team2Id = request.Team2Id,
                MapName = request.MapName,
                ServerGuid = !string.IsNullOrWhiteSpace(request.ServerGuid) ? request.ServerGuid : null,
                ServerName = request.ServerName,
                CreatedAt = DateTime.UtcNow
            };

            _context.TournamentMatches.Add(match);
            await _context.SaveChangesAsync();

            // Create response with team names from our batch query
            var team1Name = teams.First(t => t.Id == request.Team1Id).Name;
            var team2Name = teams.First(t => t.Id == request.Team2Id).Name;

            var response = new TournamentMatchResponse
            {
                Id = match.Id,
                ScheduledDate = match.ScheduledDate,
                MapName = match.MapName,
                Team1Name = team1Name,
                Team2Name = team2Name,
                ServerGuid = match.ServerGuid,
                ServerName = match.ServerName,
                RoundId = match.RoundId,
                CreatedAt = match.CreatedAt,
                Round = null
            };

            return CreatedAtAction(
                nameof(GetMatch),
                new { tournamentId = tournamentId, matchId = match.Id },
                response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating match for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error creating match" });
        }
    }

    /// <summary>
    /// Get a specific match by ID
    /// </summary>
    [HttpGet("{tournamentId}/matches/{matchId}")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResponse>> GetMatch(int tournamentId, int matchId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var match = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .Select(tm => new TournamentMatchResponse
                {
                    Id = tm.Id,
                    ScheduledDate = tm.ScheduledDate,
                    MapName = tm.MapName,
                    Team1Name = tm.Team1.Name,
                    Team2Name = tm.Team2.Name,
                    ServerGuid = tm.ServerGuid,
                    ServerName = tm.ServerName,
                    RoundId = tm.RoundId,
                    CreatedAt = tm.CreatedAt,
                    Round = tm.Round != null ? new TournamentRoundResponse
                    {
                        RoundId = tm.Round.RoundId,
                        ServerGuid = tm.Round.ServerGuid,
                        ServerName = tm.Round.ServerName,
                        MapName = tm.Round.MapName,
                        StartTime = tm.Round.StartTime,
                        EndTime = tm.Round.EndTime,
                        Tickets1 = tm.Round.Tickets1,
                        Tickets2 = tm.Round.Tickets2,
                        Team1Label = tm.Round.Team1Label,
                        Team2Label = tm.Round.Team2Label
                    } : null
                })
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            return Ok(match);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error retrieving match" });
        }
    }

    /// <summary>
    /// Update a match
    /// </summary>
    [HttpPut("{tournamentId}/matches/{matchId}")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResponse>> UpdateMatch(int tournamentId, int matchId, [FromBody] UpdateTournamentMatchRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var match = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            if (request.ScheduledDate.HasValue)
                match.ScheduledDate = request.ScheduledDate.Value;

            if (!string.IsNullOrWhiteSpace(request.MapName))
                match.MapName = request.MapName;

            var teamUpdates = new List<int>();
            if (request.Team1Id.HasValue && request.Team1Id > 0)
                teamUpdates.Add(request.Team1Id.Value);
            if (request.Team2Id.HasValue && request.Team2Id > 0)
                teamUpdates.Add(request.Team2Id.Value);

            if (teamUpdates.Count > 0)
            {
                var newTeam1Id = request.Team1Id ?? match.Team1Id;
                var newTeam2Id = request.Team2Id ?? match.Team2Id;
                
                if (newTeam1Id == newTeam2Id)
                    return BadRequest(new { message = "Team1Id and Team2Id cannot be the same" });

                var validTeams = await _context.TournamentTeams
                    .Where(tt => teamUpdates.Contains(tt.Id) && tt.TournamentId == tournamentId)
                    .Select(tt => tt.Id)
                    .ToListAsync();

                var invalidTeams = teamUpdates.Except(validTeams).ToList();
                if (invalidTeams.Any())
                    return BadRequest(new { message = $"Teams with IDs {string.Join(", ", invalidTeams)} not found in this tournament" });

                if (request.Team1Id.HasValue)
                    match.Team1Id = request.Team1Id.Value;
                if (request.Team2Id.HasValue)
                    match.Team2Id = request.Team2Id.Value;
            }

            if (request.ServerGuid != null)
            {
                if (!string.IsNullOrWhiteSpace(request.ServerGuid))
                {
                    var serverExists = await _context.Servers.AnyAsync(s => s.Guid == request.ServerGuid);
                    if (!serverExists)
                        return BadRequest(new { message = $"Server with GUID '{request.ServerGuid}' not found" });

                    match.ServerGuid = request.ServerGuid;
                }
                else
                {
                    match.ServerGuid = null;
                }
            }

            if (request.ServerName != null)
                match.ServerName = request.ServerName;

            // Handle RoundId updates - use a separate flag to indicate if RoundId should be updated
            if (request.UpdateRoundId)
            {
                if (!string.IsNullOrWhiteSpace(request.RoundId))
                {
                    var roundExists = await _context.Rounds.AnyAsync(r => r.RoundId == request.RoundId);
                    if (!roundExists)
                        return BadRequest(new { message = $"Round '{request.RoundId}' not found" });

                    match.RoundId = request.RoundId;
                }
                else
                {
                    match.RoundId = null;
                }
            }

            await _context.SaveChangesAsync();

            var response = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId)
                .Select(tm => new TournamentMatchResponse
                {
                    Id = tm.Id,
                    ScheduledDate = tm.ScheduledDate,
                    MapName = tm.MapName,
                    Team1Name = tm.Team1.Name,
                    Team2Name = tm.Team2.Name,
                    ServerGuid = tm.ServerGuid,
                    ServerName = tm.ServerName,
                    RoundId = tm.RoundId,
                    CreatedAt = tm.CreatedAt,
                    Round = tm.Round != null ? new TournamentRoundResponse
                    {
                        RoundId = tm.Round.RoundId,
                        ServerGuid = tm.Round.ServerGuid,
                        ServerName = tm.Round.ServerName,
                        MapName = tm.Round.MapName,
                        StartTime = tm.Round.StartTime,
                        EndTime = tm.Round.EndTime,
                        Tickets1 = tm.Round.Tickets1,
                        Tickets2 = tm.Round.Tickets2,
                        Team1Label = tm.Round.Team1Label,
                        Team2Label = tm.Round.Team2Label
                    } : null
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error updating match" });
        }
    }

    /// <summary>
    /// Delete a match
    /// </summary>
    [HttpDelete("{tournamentId}/matches/{matchId}")]
    [Authorize]
    public async Task<IActionResult> DeleteMatch(int tournamentId, int matchId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var match = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            _context.TournamentMatches.Remove(match);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error deleting match" });
        }
    }
}

// Request DTOs
public class CreateTournamentRequest
{
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public int? AnticipatedRoundCount { get; set; }
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
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }
}

// Team Management DTOs
public class CreateTournamentTeamRequest
{
    public string Name { get; set; } = "";
}

public class UpdateTournamentTeamRequest
{
    public string? Name { get; set; }
}

public class AddPlayerToTeamRequest
{
    public string PlayerName { get; set; } = "";
}

// Match Management DTOs
public class CreateTournamentMatchRequest
{
    public DateTime ScheduledDate { get; set; }
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    public string MapName { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
}

public class UpdateTournamentMatchRequest
{
    public DateTime? ScheduledDate { get; set; }
    public int? Team1Id { get; set; }
    public int? Team2Id { get; set; }
    public string? MapName { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? RoundId { get; set; }
    public bool UpdateRoundId { get; set; } = false;
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
    public int MatchCount { get; set; }
    public int TeamCount { get; set; }
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
    public List<TournamentTeamResponse> Teams { get; set; } = [];
    public List<TournamentMatchResponse> Matches { get; set; } = [];
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
}

public class TournamentTeamResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<TournamentTeamPlayerResponse> Players { get; set; } = [];
}

public class TournamentTeamPlayerResponse
{
    public string PlayerName { get; set; } = "";
}

public class TournamentMatchResponse
{
    public int Id { get; set; }
    public DateTime ScheduledDate { get; set; }
    public string MapName { get; set; } = "";
    public string Team1Name { get; set; } = "";
    public string Team2Name { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? RoundId { get; set; }
    public DateTime CreatedAt { get; set; }
    public TournamentRoundResponse? Round { get; set; }
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