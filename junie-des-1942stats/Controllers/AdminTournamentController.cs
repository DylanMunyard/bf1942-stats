using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Services;
using junie_des_1942stats.Services.Tournament;
using Microsoft.Extensions.Logging;
using NodaTime;
using Markdig;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/admin/tournaments")]
public class AdminTournamentController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly ILogger<AdminTournamentController> _logger;
    private readonly IMarkdownSanitizationService _markdownSanitizer;
    private readonly ITournamentMatchResultService _matchResultService;
    private readonly ITeamRankingCalculator _rankingCalculator;

    public AdminTournamentController(
        PlayerTrackerDbContext context,
        ILogger<AdminTournamentController> logger,
        IMarkdownSanitizationService markdownSanitizer,
        ITournamentMatchResultService matchResultService,
        ITeamRankingCalculator rankingCalculator)
    {
        _context = context;
        _logger = logger;
        _markdownSanitizer = markdownSanitizer;
        _matchResultService = matchResultService;
        _rankingCalculator = rankingCalculator;
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
                .Include(t => t.Theme)
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
                HasCommunityLogo = t.CommunityLogo != null,
                HasRules = !string.IsNullOrEmpty(t.Rules),
                ServerGuid = t.ServerGuid,
                ServerName = t.Server?.Name,
                DiscordUrl = t.DiscordUrl,
                ForumUrl = t.ForumUrl,
                Theme = t.Theme != null ? new TournamentThemeResponse
                {
                    Id = t.Theme.Id,
                    BackgroundColour = t.Theme.BackgroundColour,
                    TextColour = t.Theme.TextColour,
                    AccentColour = t.Theme.AccentColour
                } : null
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
                .Include(t => t.Theme)
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

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

            // Load matches with team names and maps
            var matchResponses = await _context.TournamentMatches
                .Where(tm => tm.TournamentId == id)
                .Select(tm => new TournamentMatchResponse
                {
                    Id = tm.Id,
                    ScheduledDate = tm.ScheduledDate,
                    Team1Id = tm.Team1Id,
                    Team1Name = tm.Team1.Name,
                    Team2Id = tm.Team2Id,
                    Team2Name = tm.Team2.Name,
                    ServerGuid = tm.ServerGuid,
                    ServerName = tm.ServerName,
                    Week = tm.Week,
                    CreatedAt = tm.CreatedAt,
                    Maps = tm.Maps.OrderBy(m => m.MapOrder).Select(m => new TournamentMatchMapResponse
                    {
                        Id = m.Id,
                        MapName = m.MapName,
                        MapOrder = m.MapOrder,
                        RoundId = m.RoundId,
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                        Round = m.Round != null ? new TournamentRoundResponse
                        {
                            RoundId = m.Round.RoundId,
                            ServerGuid = m.Round.ServerGuid,
                            ServerName = m.Round.ServerName,
                            MapName = m.Round.MapName,
                            StartTime = m.Round.StartTime,
                            EndTime = m.Round.EndTime,
                            Tickets1 = m.Round.Tickets1,
                            Tickets2 = m.Round.Tickets2,
                            Team1Label = m.Round.Team1Label,
                            Team2Label = m.Round.Team2Label
                        } : null,
                        MatchResult = m.MatchResult != null ? new TournamentMatchResultResponse
                        {
                            Id = m.MatchResult.Id,
                            Team1Id = m.MatchResult.Team1Id,
                            Team1Name = m.MatchResult.Team1 != null ? m.MatchResult.Team1.Name : null,
                            Team2Id = m.MatchResult.Team2Id,
                            Team2Name = m.MatchResult.Team2 != null ? m.MatchResult.Team2.Name : null,
                            WinningTeamId = m.MatchResult.WinningTeamId,
                            WinningTeamName = m.MatchResult.WinningTeam != null ? m.MatchResult.WinningTeam.Name : null,
                            Team1Tickets = m.MatchResult.Team1Tickets,
                            Team2Tickets = m.MatchResult.Team2Tickets
                        } : null
                    }).ToList()
                })
                .OrderBy(tm => tm.ScheduledDate)
                .ToListAsync();

            // Group matches by week
            var matchesByWeek = matchResponses
                .GroupBy(m => m.Week)
                .OrderBy(g => g.Key)
                .Select(g => new MatchWeekGroup
                {
                    Week = g.Key,
                    Matches = g.ToList()
                })
                .ToList();

            var themeResponse = tournament.Theme != null ? new TournamentThemeResponse
            {
                Id = tournament.Theme.Id,
                BackgroundColour = tournament.Theme.BackgroundColour,
                TextColour = tournament.Theme.TextColour,
                AccentColour = tournament.Theme.AccentColour
            } : null;

            var response = new TournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                Game = tournament.Game,
                CreatedAt = tournament.CreatedAt,
                AnticipatedRoundCount = tournament.AnticipatedRoundCount,
                Teams = teams,
                MatchesByWeek = matchesByWeek,
                HasHeroImage = tournament.HeroImage != null,
                HasCommunityLogo = tournament.CommunityLogo != null,
                Rules = tournament.Rules,
                ServerGuid = tournament.ServerGuid,
                ServerName = tournament.Server?.Name,
                DiscordUrl = tournament.DiscordUrl,
                ForumUrl = tournament.ForumUrl,
                Theme = themeResponse
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

            var (heroImageData, heroImageError) = ValidateAndProcessImage(request.HeroImageBase64, request.HeroImageContentType);
            if (heroImageError != null)
                return BadRequest(new { message = heroImageError });

            var (communityLogoData, logoImageError) = ValidateAndProcessImage(request.CommunityLogoBase64, request.CommunityLogoContentType);
            if (logoImageError != null)
                return BadRequest(new { message = logoImageError });

            // Validate theme if provided
            if (request.Theme != null)
            {
                var (isValid, themeError) = ValidateTheme(request.Theme);
                if (!isValid)
                    return BadRequest(new { message = themeError });
            }

            // Validate and store rules as markdown
            string? sanitizedRules = null;
            if (!string.IsNullOrWhiteSpace(request.Rules))
            {
                // Validate markdown for XSS risks
                var validationResult = _markdownSanitizer.ValidateMarkdown(request.Rules);
                if (!validationResult.IsValid)
                    return BadRequest(new { message = validationResult.Error });

                // Store the raw markdown (safe to store due to validation)
                // The UI will handle rendering the markdown
                sanitizedRules = request.Rules;
            }

            var tournament = new Tournament
            {
                Name = request.Name,
                Organizer = request.Organizer,
                Game = request.Game.ToLower(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                CreatedByUserId = user.Id,
                CreatedByUserEmail = userEmail,
                AnticipatedRoundCount = request.AnticipatedRoundCount,
                HeroImage = heroImageData,
                HeroImageContentType = heroImageData != null ? request.HeroImageContentType : null,
                CommunityLogo = communityLogoData,
                CommunityLogoContentType = communityLogoData != null ? request.CommunityLogoContentType : null,
                Rules = sanitizedRules,
                ServerGuid = !string.IsNullOrWhiteSpace(request.ServerGuid) ? request.ServerGuid : null,
                DiscordUrl = request.DiscordUrl,
                ForumUrl = request.ForumUrl
            };

            _context.Tournaments.Add(tournament);

            // Create theme if provided
            if (request.Theme != null)
            {
                var theme = new TournamentTheme
                {
                    BackgroundColour = request.Theme.BackgroundColour,
                    TextColour = request.Theme.TextColour,
                    AccentColour = request.Theme.AccentColour,
                    Tournament = tournament
                };
                _context.Add(theme);
            }

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
                .Include(t => t.Theme)
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

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

            if (request.RemoveHeroImage)
            {
                tournament.HeroImage = null;
                tournament.HeroImageContentType = null;
            }
            else if (request.HeroImageBase64 != null)
            {
                var (heroImageData, heroImageError) = ValidateAndProcessImage(request.HeroImageBase64, request.HeroImageContentType);
                if (heroImageError != null)
                    return BadRequest(new { message = heroImageError });

                tournament.HeroImage = heroImageData;
                tournament.HeroImageContentType = heroImageData != null ? request.HeroImageContentType : null;
            }

            if (request.RemoveCommunityLogo)
            {
                tournament.CommunityLogo = null;
                tournament.CommunityLogoContentType = null;
            }
            else if (request.CommunityLogoBase64 != null)
            {
                var (communityLogoData, logoImageError) = ValidateAndProcessImage(request.CommunityLogoBase64, request.CommunityLogoContentType);
                if (logoImageError != null)
                    return BadRequest(new { message = logoImageError });

                tournament.CommunityLogo = communityLogoData;
                tournament.CommunityLogoContentType = communityLogoData != null ? request.CommunityLogoContentType : null;
            }

            if (request.Rules != null)
            {
                // Validate and store rules as markdown
                string? sanitizedRules = null;
                if (!string.IsNullOrWhiteSpace(request.Rules))
                {
                    // Validate markdown for XSS risks
                    var validationResult = _markdownSanitizer.ValidateMarkdown(request.Rules);
                    if (!validationResult.IsValid)
                        return BadRequest(new { message = validationResult.Error });

                    // Store the raw markdown (safe to store due to validation)
                    // The UI will handle rendering the markdown
                    sanitizedRules = request.Rules;
                }

                tournament.Rules = sanitizedRules;
            }

            if (request.DiscordUrl != null)
                tournament.DiscordUrl = request.DiscordUrl;

            if (request.ForumUrl != null)
                tournament.ForumUrl = request.ForumUrl;

            // Handle theme updates
            if (request.Theme != null)
            {
                var (isValid, themeError) = ValidateTheme(request.Theme);
                if (!isValid)
                    return BadRequest(new { message = themeError });

                if (tournament.Theme == null)
                {
                    // Create new theme
                    tournament.Theme = new TournamentTheme
                    {
                        BackgroundColour = request.Theme.BackgroundColour,
                        TextColour = request.Theme.TextColour,
                        AccentColour = request.Theme.AccentColour,
                        Tournament = tournament
                    };
                    _context.Add(tournament.Theme);
                }
                else
                {
                    // Update existing theme
                    tournament.Theme.BackgroundColour = request.Theme.BackgroundColour;
                    tournament.Theme.TextColour = request.Theme.TextColour;
                    tournament.Theme.AccentColour = request.Theme.AccentColour;
                }
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
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

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

    /// <summary>
    /// Get tournament community logo
    /// </summary>
    [HttpGet("{id}/logo")]
    [Authorize]
    public async Task<IActionResult> GetTournamentLogo(int id)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await _context.Tournaments
                .Where(t => t.Id == id && t.CreatedByUserEmail == userEmail)
                .Select(t => new { t.CommunityLogo, t.CommunityLogoContentType })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (tournament.CommunityLogo == null)
                return NotFound(new { message = "Tournament has no community logo" });

            return File(tournament.CommunityLogo, tournament.CommunityLogoContentType ?? "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tournament logo {TournamentId}", id);
            return StatusCode(500, new { message = "Error retrieving tournament logo" });
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

    /// <summary>
    /// Cleanup orphaned match results - removes MatchResults whose maps no longer exist
    /// This handles cases where cascade delete didn't work properly in previous versions
    /// </summary>
    private async Task<int> CleanupOrphanedMatchResultsAsync(int tournamentId)
    {
        // Find all MatchResults in this tournament that reference non-existent maps
        var orphanedResults = await _context.TournamentMatchResults
            .Where(mr => mr.TournamentId == tournamentId)
            .Where(mr => !_context.TournamentMatchMaps.Any(m => m.Id == mr.MapId))
            .ToListAsync();

        if (orphanedResults.Count > 0)
        {
            _logger.LogWarning(
                "Found {Count} orphaned match results in tournament {TournamentId}. Cleaning up...",
                orphanedResults.Count, tournamentId);

            foreach (var result in orphanedResults)
            {
                _logger.LogInformation(
                    "Deleting orphaned match result {ResultId} (referenced non-existent map {MapId})",
                    result.Id, result.MapId);
                _context.TournamentMatchResults.Remove(result);
            }

            await _context.SaveChangesAsync();
        }

        return orphanedResults.Count;
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

    private (bool isValid, string? error) ValidateTheme(TournamentThemeRequest theme)
    {
        // All colors are optional, but if provided they must be valid hex colors
        if (!string.IsNullOrWhiteSpace(theme.BackgroundColour) && !IsValidHexColour(theme.BackgroundColour))
            return (false, "Invalid BackgroundColour. Use hex like #RRGGBB or #RRGGBBAA.");

        if (!string.IsNullOrWhiteSpace(theme.TextColour) && !IsValidHexColour(theme.TextColour))
            return (false, "Invalid TextColour. Use hex like #RRGGBB or #RRGGBBAA.");

        if (!string.IsNullOrWhiteSpace(theme.AccentColour) && !IsValidHexColour(theme.AccentColour))
            return (false, "Invalid AccentColour. Use hex like #RRGGBB or #RRGGBBAA.");

        return (true, null);
    }

    private bool IsValidHexColour(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        // Allows #RGB, #RRGGBB, #RGBA, #RRGGBBAA
        if (!input.StartsWith('#')) return false;
        var len = input.Length;
        return len == 4 || len == 5 || len == 7 || len == 9;
    }

    private async Task<TournamentDetailResponse> GetTournamentDetailOptimizedAsync(int tournamentId)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.OrganizerPlayer)
            .Include(t => t.Server)
            .Include(t => t.Theme)
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
                Team1Id = tm.Team1Id,
                Team1Name = tm.Team1.Name,
                Team2Id = tm.Team2Id,
                Team2Name = tm.Team2.Name,
                ServerGuid = tm.ServerGuid,
                ServerName = tm.ServerName,
                Week = tm.Week,
                CreatedAt = tm.CreatedAt,
                Maps = tm.Maps.OrderBy(m => m.MapOrder).Select(m => new TournamentMatchMapResponse
                {
                    Id = m.Id,
                    MapName = m.MapName,
                    MapOrder = m.MapOrder,
                    RoundId = m.RoundId,
                    Round = m.Round != null ? new TournamentRoundResponse
                    {
                        RoundId = m.Round.RoundId,
                        ServerGuid = m.Round.ServerGuid,
                        ServerName = m.Round.ServerName,
                        MapName = m.Round.MapName,
                        StartTime = m.Round.StartTime,
                        EndTime = m.Round.EndTime,
                        Tickets1 = m.Round.Tickets1,
                        Tickets2 = m.Round.Tickets2,
                        Team1Label = m.Round.Team1Label,
                        Team2Label = m.Round.Team2Label
                    } : null,
                    MatchResult = m.MatchResult != null ? new TournamentMatchResultResponse
                    {
                        Id = m.MatchResult.Id,
                        Team1Id = m.MatchResult.Team1Id,
                        Team1Name = m.MatchResult.Team1 != null ? m.MatchResult.Team1.Name : null,
                        Team2Id = m.MatchResult.Team2Id,
                        Team2Name = m.MatchResult.Team2 != null ? m.MatchResult.Team2.Name : null,
                        WinningTeamId = m.MatchResult.WinningTeamId,
                        WinningTeamName = m.MatchResult.WinningTeam != null ? m.MatchResult.WinningTeam.Name : null,
                        Team1Tickets = m.MatchResult.Team1Tickets,
                        Team2Tickets = m.MatchResult.Team2Tickets
                    } : null
                }).ToList()
            })
            .OrderBy(tm => tm.ScheduledDate)
            .ToListAsync();

        // Group matches by week
        var matchesByWeek = matches
            .GroupBy(m => m.Week)
            .OrderBy(g => g.Key)
            .Select(g => new MatchWeekGroup
            {
                Week = g.Key,
                Matches = g.ToList()
            })
            .ToList();

        var themeResponse = tournament.Theme != null ? new TournamentThemeResponse
        {
            Id = tournament.Theme.Id,
            BackgroundColour = tournament.Theme.BackgroundColour,
            TextColour = tournament.Theme.TextColour,
            AccentColour = tournament.Theme.AccentColour
        } : null;

        return new TournamentDetailResponse
        {
            Id = tournament.Id,
            Name = tournament.Name,
            Organizer = tournament.Organizer,
            Game = tournament.Game,
            CreatedAt = tournament.CreatedAt,
            AnticipatedRoundCount = tournament.AnticipatedRoundCount,
            Teams = teams,
            MatchesByWeek = matchesByWeek,
            HasHeroImage = tournament.HeroImage != null,
            HasCommunityLogo = tournament.CommunityLogo != null,
            Rules = tournament.Rules,
            ServerGuid = tournament.ServerGuid,
            ServerName = tournament.Server?.Name,
            DiscordUrl = tournament.DiscordUrl,
            ForumUrl = tournament.ForumUrl,
            Theme = themeResponse
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
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

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
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
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
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (request.Team1Id <= 0 || request.Team2Id <= 0)
                return BadRequest(new { message = "Both Team1Id and Team2Id are required" });

            if (request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team1Id and Team2Id cannot be the same" });

            if (request.MapNames == null || request.MapNames.Count == 0)
                return BadRequest(new { message = "At least one map name is required" });

            // Validate all map names are non-empty
            if (request.MapNames.Any(string.IsNullOrWhiteSpace))
                return BadRequest(new { message = "All map names must be non-empty" });

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
                ServerGuid = !string.IsNullOrWhiteSpace(request.ServerGuid) ? request.ServerGuid : null,
                ServerName = request.ServerName,
                Week = request.Week,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            _context.TournamentMatches.Add(match);
            await _context.SaveChangesAsync();

            // Create map entries
            var maps = request.MapNames.Select((mapName, index) => new TournamentMatchMap
            {
                MatchId = match.Id,
                MapName = mapName,
                MapOrder = index,
                RoundId = null
            }).ToList();

            _context.TournamentMatchMaps.AddRange(maps);
            await _context.SaveChangesAsync();

            // Create response with team names from our batch query
            var team1Name = teams.First(t => t.Id == request.Team1Id).Name;
            var team2Name = teams.First(t => t.Id == request.Team2Id).Name;

            var response = new TournamentMatchResponse
            {
                Id = match.Id,
                ScheduledDate = match.ScheduledDate,
                Team1Id = match.Team1Id,
                Team1Name = team1Name,
                Team2Id = match.Team2Id,
                Team2Name = team2Name,
                ServerGuid = match.ServerGuid,
                ServerName = match.ServerName,
                Week = match.Week,
                CreatedAt = match.CreatedAt,
                Maps = maps.Select(m => new TournamentMatchMapResponse
                {
                    Id = m.Id,
                    MapName = m.MapName,
                    MapOrder = m.MapOrder,
                    RoundId = m.RoundId,
                    TeamId = m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    Round = null
                }).ToList()
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
                    Team1Id = tm.Team1Id,
                    Team1Name = tm.Team1.Name,
                    Team2Id = tm.Team2Id,
                    Team2Name = tm.Team2.Name,
                    ServerGuid = tm.ServerGuid,
                    ServerName = tm.ServerName,
                    Week = tm.Week,
                    CreatedAt = tm.CreatedAt,
                    Maps = tm.Maps.OrderBy(m => m.MapOrder).Select(m => new TournamentMatchMapResponse
                    {
                        Id = m.Id,
                        MapName = m.MapName,
                        MapOrder = m.MapOrder,
                        RoundId = m.RoundId,
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                        Round = m.Round != null ? new TournamentRoundResponse
                        {
                            RoundId = m.Round.RoundId,
                            ServerGuid = m.Round.ServerGuid,
                            ServerName = m.Round.ServerName,
                            MapName = m.Round.MapName,
                            StartTime = m.Round.StartTime,
                            EndTime = m.Round.EndTime,
                            Tickets1 = m.Round.Tickets1,
                            Tickets2 = m.Round.Tickets2,
                            Team1Label = m.Round.Team1Label,
                            Team2Label = m.Round.Team2Label
                        } : null,
                        MatchResult = m.MatchResult != null ? new TournamentMatchResultResponse
                        {
                            Id = m.MatchResult.Id,
                            Team1Id = m.MatchResult.Team1Id,
                            Team1Name = m.MatchResult.Team1 != null ? m.MatchResult.Team1.Name : null,
                            Team2Id = m.MatchResult.Team2Id,
                            Team2Name = m.MatchResult.Team2 != null ? m.MatchResult.Team2.Name : null,
                            WinningTeamId = m.MatchResult.WinningTeamId,
                            WinningTeamName = m.MatchResult.WinningTeam != null ? m.MatchResult.WinningTeam.Name : null,
                            Team1Tickets = m.MatchResult.Team1Tickets,
                            Team2Tickets = m.MatchResult.Team2Tickets
                        } : null
                    }).ToList()
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

            if (request.Week != null)
                match.Week = request.Week;

            // Handle map updates
            if (request.MapNames != null)
            {
                if (request.MapNames.Count == 0)
                    return BadRequest(new { message = "At least one map name is required" });

                // Validate all map names are non-empty
                if (request.MapNames.Any(string.IsNullOrWhiteSpace))
                    return BadRequest(new { message = "All map names must be non-empty" });

                // Load existing maps WITH their MatchResults to ensure cascade delete works
                var existingMaps = await _context.TournamentMatchMaps
                    .Include(m => m.MatchResult)
                    .Where(tmm => tmm.MatchId == matchId)
                    .ToListAsync();

                // Build a dictionary of MapName -> (RoundId, MapId) from existing maps
                var mapNameToData = existingMaps
                    .ToDictionary(m => m.MapName, m => new { m.RoundId, m.Id });

                // Identify which maps are being removed vs kept
                var mapsToRemove = existingMaps
                    .Where(m => !request.MapNames.Contains(m.MapName))
                    .ToList();

                var mapsToKeep = existingMaps
                    .Where(m => request.MapNames.Contains(m.MapName))
                    .ToList();

                // Explicitly delete MatchResults for removed maps (ensures proper cleanup)
                foreach (var mapToRemove in mapsToRemove)
                {
                    if (mapToRemove.MatchResult != null)
                    {
                        _logger.LogInformation(
                            "Deleting orphaned match result {ResultId} when removing map {MapId} from match {MatchId}",
                            mapToRemove.MatchResult.Id, mapToRemove.Id, matchId);
                        _context.TournamentMatchResults.Remove(mapToRemove.MatchResult);
                    }
                    _context.TournamentMatchMaps.Remove(mapToRemove);
                }

                // Update existing maps that are being kept (preserve RoundId and MatchResult)
                var newMapOrder = 0;
                foreach (var mapName in request.MapNames)
                {
                    var existingMap = mapsToKeep.FirstOrDefault(m => m.MapName == mapName);
                    if (existingMap != null)
                    {
                        // Update map order only if it changed
                        if (existingMap.MapOrder != newMapOrder)
                        {
                            _logger.LogInformation(
                                "Updating map order for map {MapId} from {OldOrder} to {NewOrder}",
                                existingMap.Id, existingMap.MapOrder, newMapOrder);
                            existingMap.MapOrder = newMapOrder;
                        }
                    }
                    else
                    {
                        // This is a new map - add it without a RoundId (user can link round later)
                        var newMap = new TournamentMatchMap
                        {
                            MatchId = matchId,
                            MapName = mapName,
                            MapOrder = newMapOrder,
                            RoundId = null // New maps don't have rounds until explicitly linked
                        };
                        _logger.LogInformation(
                            "Adding new map '{MapName}' at order {MapOrder} to match {MatchId}",
                            mapName, newMapOrder, matchId);
                        _context.TournamentMatchMaps.Add(newMap);
                    }
                    newMapOrder++;
                }
            }

            await _context.SaveChangesAsync();

            var response = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId)
                .Select(tm => new TournamentMatchResponse
                {
                    Id = tm.Id,
                    ScheduledDate = tm.ScheduledDate,
                    Team1Id = tm.Team1Id,
                    Team1Name = tm.Team1.Name,
                    Team2Id = tm.Team2Id,
                    Team2Name = tm.Team2.Name,
                    ServerGuid = tm.ServerGuid,
                    ServerName = tm.ServerName,
                    Week = tm.Week,
                    CreatedAt = tm.CreatedAt,
                    Maps = tm.Maps.OrderBy(m => m.MapOrder).Select(m => new TournamentMatchMapResponse
                    {
                        Id = m.Id,
                        MapName = m.MapName,
                        MapOrder = m.MapOrder,
                        RoundId = m.RoundId,
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                        Round = m.Round != null ? new TournamentRoundResponse
                        {
                            RoundId = m.Round.RoundId,
                            ServerGuid = m.Round.ServerGuid,
                            ServerName = m.Round.ServerName,
                            MapName = m.Round.MapName,
                            StartTime = m.Round.StartTime,
                            EndTime = m.Round.EndTime,
                            Tickets1 = m.Round.Tickets1,
                            Tickets2 = m.Round.Tickets2,
                            Team1Label = m.Round.Team1Label,
                            Team2Label = m.Round.Team2Label
                        } : null,
                        MatchResult = m.MatchResult != null ? new TournamentMatchResultResponse
                        {
                            Id = m.MatchResult.Id,
                            Team1Id = m.MatchResult.Team1Id,
                            Team1Name = m.MatchResult.Team1 != null ? m.MatchResult.Team1.Name : null,
                            Team2Id = m.MatchResult.Team2Id,
                            Team2Name = m.MatchResult.Team2 != null ? m.MatchResult.Team2.Name : null,
                            WinningTeamId = m.MatchResult.WinningTeamId,
                            WinningTeamName = m.MatchResult.WinningTeam != null ? m.MatchResult.WinningTeam.Name : null,
                            Team1Tickets = m.MatchResult.Team1Tickets,
                            Team2Tickets = m.MatchResult.Team2Tickets
                        } : null
                    }).ToList()
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

    /// <summary>
    /// Update a tournament match map (e.g., link a round to a map)
    /// When a RoundId is assigned, automatically creates/updates the match result with team mapping
    /// </summary>
    [HttpPut("{tournamentId}/matches/{matchId}/maps/{mapId}")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchMapResponse>> UpdateMatchMap(
        int tournamentId,
        int matchId,
        int mapId,
        [FromBody] UpdateTournamentMatchMapRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify the match belongs to this tournament and user owns it
            var match = await _context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            var map = await _context.TournamentMatchMaps
                .Where(tmm => tmm.Id == mapId && tmm.MatchId == matchId)
                .FirstOrDefaultAsync();

            if (map == null)
                return NotFound(new { message = "Map not found" });

            if (!string.IsNullOrWhiteSpace(request.MapName))
                map.MapName = request.MapName;

            string? teamMappingWarning = null;

            // Handle RoundId updates
            if (request.UpdateRoundId)
            {
                if (!string.IsNullOrWhiteSpace(request.RoundId))
                {
                    var roundExists = await _context.Rounds.AnyAsync(r => r.RoundId == request.RoundId);
                    if (!roundExists)
                        return BadRequest(new { message = $"Round '{request.RoundId}' not found" });

                    map.RoundId = request.RoundId;

                    // Create/update match result with team mapping
                    _logger.LogInformation(
                        "Processing match result for tournament {TournamentId}, match {MatchId}, map {MapId}, round {RoundId}",
                        tournamentId, matchId, mapId, request.RoundId);

                    var (resultId, warning) = await _matchResultService.CreateOrUpdateMatchResultAsync(
                        tournamentId, matchId, mapId, request.RoundId);

                    if (warning != null)
                    {
                        teamMappingWarning = warning;
                        _logger.LogWarning(
                            "Team mapping warning for tournament {TournamentId}, match {MatchId}: {Warning}",
                            tournamentId, matchId, warning);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Match result created/updated with ID {ResultId} for tournament {TournamentId}, match {MatchId}",
                            resultId, tournamentId, matchId);

                        // Trigger ranking recalculation asynchronously
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                _logger.LogInformation(
                                    "Starting async ranking recalculation for tournament {TournamentId}",
                                    tournamentId);
                                await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                                _logger.LogInformation(
                                    "Completed async ranking recalculation for tournament {TournamentId}",
                                    tournamentId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Error during async ranking recalculation for tournament {TournamentId}",
                                    tournamentId);
                            }
                        });
                    }
                }
                else
                {
                    // Unlinking a round - delete the associated match result
                    map.RoundId = null;

                    var existingResult = await _context.TournamentMatchResults
                        .Where(mr => mr.MapId == mapId)
                        .FirstOrDefaultAsync();

                    if (existingResult != null)
                    {
                        _logger.LogInformation(
                            "Deleting match result {ResultId} for map {MapId} (unlinking round)",
                            existingResult.Id, mapId);

                        _context.TournamentMatchResults.Remove(existingResult);

                        // Trigger ranking recalculation asynchronously since we removed a result
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                _logger.LogInformation(
                                    "Starting async ranking recalculation after result deletion for tournament {TournamentId}",
                                    tournamentId);
                                await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                                _logger.LogInformation(
                                    "Completed async ranking recalculation after result deletion for tournament {TournamentId}",
                                    tournamentId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Error during async ranking recalculation for tournament {TournamentId}",
                                    tournamentId);
                            }
                        });
                    }
                }
            }

            // Handle TeamId updates
            if (request.TeamId.HasValue)
            {
                // Verify the team belongs to this tournament
                var teamExists = await _context.TournamentTeams
                    .AnyAsync(tt => tt.Id == request.TeamId.Value && tt.TournamentId == tournamentId);

                if (!teamExists)
                    return BadRequest(new { message = $"Team {request.TeamId} not found in this tournament" });

                map.TeamId = request.TeamId;
            }

            await _context.SaveChangesAsync();

            var response = await _context.TournamentMatchMaps
                .Where(tmm => tmm.Id == mapId)
                .Select(m => new TournamentMatchMapResponse
                {
                    Id = m.Id,
                    MapName = m.MapName,
                    MapOrder = m.MapOrder,
                    RoundId = m.RoundId,
                    TeamId = m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    Round = m.Round != null ? new TournamentRoundResponse
                    {
                        RoundId = m.Round.RoundId,
                        ServerGuid = m.Round.ServerGuid,
                        ServerName = m.Round.ServerName,
                        MapName = m.Round.MapName,
                        StartTime = m.Round.StartTime,
                        EndTime = m.Round.EndTime,
                        Tickets1 = m.Round.Tickets1,
                        Tickets2 = m.Round.Tickets2,
                        Team1Label = m.Round.Team1Label,
                        Team2Label = m.Round.Team2Label
                    } : null,
                    MatchResult = m.MatchResult != null ? new TournamentMatchResultResponse
                    {
                        Id = m.MatchResult.Id,
                        Team1Id = m.MatchResult.Team1Id,
                        Team1Name = m.MatchResult.Team1 != null ? m.MatchResult.Team1.Name : null,
                        Team2Id = m.MatchResult.Team2Id,
                        Team2Name = m.MatchResult.Team2 != null ? m.MatchResult.Team2.Name : null,
                        WinningTeamId = m.MatchResult.WinningTeamId,
                        WinningTeamName = m.MatchResult.WinningTeam != null ? m.MatchResult.WinningTeam.Name : null,
                        Team1Tickets = m.MatchResult.Team1Tickets,
                        Team2Tickets = m.MatchResult.Team2Tickets
                    } : null
                })
                .FirstAsync();

            // Include warning in response if team mapping had issues
            var result = new { response, teamMappingWarning };
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating map {MapId} for match {MatchId}", mapId, matchId);
            return StatusCode(500, new { message = "Error updating map" });
        }
    }

    /// <summary>
    /// Delete a tournament match
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

    // ===== TOURNAMENT LEADERBOARD ENDPOINTS =====

    /// <summary>
    /// Get leaderboard rankings for a tournament (week-specific or cumulative)
    /// </summary>
    [HttpGet("{tournamentId}/leaderboard")]
    [Authorize]
    public async Task<ActionResult<List<TournamentTeamRankingResponse>>> GetLeaderboard(
        int tournamentId,
        string? week = null)
    {
        try
        {
            // Verify tournament belongs to user
            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == User.FindFirstValue(ClaimTypes.Email) && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get rankings from database - Week == week OR (Week == null AND week == null) for cumulative
            var rankings = await _context.TournamentTeamRankings
                .Where(r => r.TournamentId == tournamentId && r.Week == week)
                .OrderBy(r => r.Rank)
                .Select(r => new TournamentTeamRankingResponse
                {
                    Rank = r.Rank,
                    TeamId = r.TeamId,
                    TeamName = r.Team.Name,
                    RoundsWon = r.RoundsWon,
                    RoundsTied = r.RoundsTied,
                    RoundsLost = r.RoundsLost,
                    TicketDifferential = r.TicketDifferential,
                    Week = r.Week
                })
                .ToListAsync();

            return Ok(rankings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting leaderboard for tournament {TournamentId}, week {Week}", tournamentId, week);
            return StatusCode(500, new { message = "Error retrieving leaderboard" });
        }
    }

    /// <summary>
    /// Override team mapping for a match result (admin manual correction)
    /// </summary>
    [HttpPut("{tournamentId}/match-results/{resultId}/override-teams")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResultAdminResponse>> OverrideTeamMapping(
        int tournamentId,
        int resultId,
        [FromBody] OverrideTeamMappingRequest request)
    {
        try
        {
            // Verify tournament belongs to user
            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == User.FindFirstValue(ClaimTypes.Email) && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get result and verify it belongs to the tournament
            var result = await _context.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.Id == resultId && r.TournamentId == tournamentId);

            if (result == null)
                return NotFound(new { message = "Match result not found" });

            // Validate team assignments
            if (request.Team1Id <= 0 || request.Team2Id <= 0)
                return BadRequest(new { message = "Both Team1Id and Team2Id must be provided and greater than 0" });

            if (request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team1Id and Team2Id cannot be the same" });

            _logger.LogInformation(
                "Overriding team mapping for match result {ResultId} in tournament {TournamentId}",
                resultId, tournamentId);

            // Use service to override
            await _matchResultService.OverrideTeamMappingAsync(resultId, request.Team1Id, request.Team2Id);

            // Trigger ranking recalculation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Starting async ranking recalculation after team override for tournament {TournamentId}",
                        tournamentId);
                    await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                    _logger.LogInformation(
                        "Completed async ranking recalculation after team override for tournament {TournamentId}",
                        tournamentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error during async ranking recalculation for tournament {TournamentId}",
                        tournamentId);
                }
            });

            // Return updated result
            var updatedResult = await _matchResultService.GetMatchResultAsync(resultId);
            var response = new TournamentMatchResultAdminResponse
            {
                Id = updatedResult!.Id,
                TournamentId = updatedResult.TournamentId,
                MatchId = updatedResult.MatchId,
                MapId = updatedResult.MapId,
                RoundId = updatedResult.RoundId,
                Week = updatedResult.Week,
                Team1Id = updatedResult.Team1Id,
                Team1Name = updatedResult.Team1?.Name,
                Team2Id = updatedResult.Team2Id,
                Team2Name = updatedResult.Team2?.Name,
                WinningTeamId = updatedResult.WinningTeamId,
                WinningTeamName = updatedResult.WinningTeam?.Name,
                Team1Tickets = updatedResult.Team1Tickets,
                Team2Tickets = updatedResult.Team2Tickets,
                UpdatedAt = updatedResult.UpdatedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error overriding team mapping for result {ResultId}", resultId);
            return StatusCode(500, new { message = "Error overriding team mapping" });
        }
    }

    /// <summary>
    /// Delete a match result
    /// </summary>
    [HttpDelete("{tournamentId}/match-results/{resultId}")]
    [Authorize]
    public async Task<IActionResult> DeleteMatchResult(int tournamentId, int resultId)
    {
        try
        {
            // Verify tournament belongs to user
            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == User.FindFirstValue(ClaimTypes.Email) && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get result and verify it belongs to the tournament
            var result = await _context.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.Id == resultId && r.TournamentId == tournamentId);

            if (result == null)
                return NotFound(new { message = "Match result not found" });

            _logger.LogInformation(
                "Deleting match result {ResultId} from tournament {TournamentId}",
                resultId, tournamentId);

            await _matchResultService.DeleteMatchResultAsync(resultId);

            // Trigger ranking recalculation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Starting async ranking recalculation after result deletion for tournament {TournamentId}",
                        tournamentId);
                    await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                    _logger.LogInformation(
                        "Completed async ranking recalculation after result deletion for tournament {TournamentId}",
                        tournamentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error during async ranking recalculation for tournament {TournamentId}",
                        tournamentId);
                }
            });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting match result {ResultId}", resultId);
            return StatusCode(500, new { message = "Error deleting match result" });
        }
    }

    /// <summary>
    /// Manually trigger ranking recalculation for a tournament
    /// </summary>
    [HttpPost("{tournamentId}/leaderboard/recalculate")]
    [Authorize]
    public async Task<ActionResult<RecalculateRankingsResponse>> RecalculateRankings(int tournamentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            _logger.LogInformation(
                "Manual ranking recalculation triggered for tournament {TournamentId}",
                tournamentId);

            var totalUpdated = await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);

            return Ok(new RecalculateRankingsResponse
            {
                TournamentId = tournamentId,
                TotalRankingsUpdated = totalUpdated,
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating rankings for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error recalculating rankings" });
        }
    }

    /// <summary>
    /// Cleanup orphaned match results for a tournament
    /// This removes MatchResult records whose maps no longer exist
    /// Useful for fixing data consistency issues from previous versions
    /// </summary>
    [HttpPost("{tournamentId}/maintenance/cleanup-orphaned-results")]
    [Authorize]
    public async Task<ActionResult<CleanupOrphanedResultsResponse>> CleanupOrphanedResults(int tournamentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await _context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            _logger.LogInformation(
                "Manual cleanup of orphaned match results triggered for tournament {TournamentId}",
                tournamentId);

            var orphanedCount = await CleanupOrphanedMatchResultsAsync(tournamentId);

            if (orphanedCount > 0)
            {
                _logger.LogInformation(
                    "Cleaned up {Count} orphaned match results from tournament {TournamentId}. Recalculating rankings...",
                    orphanedCount, tournamentId);

                // Recalculate rankings after cleanup
                await _rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
            }

            return Ok(new CleanupOrphanedResultsResponse
            {
                TournamentId = tournamentId,
                OrphanedResultsRemoved = orphanedCount,
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up orphaned results for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error cleaning up orphaned results" });
        }
    }
}

// Request DTOs
// Theme DTOs
public class TournamentThemeRequest
{
    public string? BackgroundColour { get; set; } // Hex color
    public string? TextColour { get; set; } // Hex color
    public string? AccentColour { get; set; } // Hex color
}

public class TournamentThemeResponse
{
    public int Id { get; set; }
    public string? BackgroundColour { get; set; } // Hex color
    public string? TextColour { get; set; } // Hex color
    public string? AccentColour { get; set; } // Hex color
}

public class CreateTournamentRequest
{
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public int? AnticipatedRoundCount { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? CommunityLogoBase64 { get; set; }
    public string? CommunityLogoContentType { get; set; }
    public string? Rules { get; set; }
    public string? ServerGuid { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public TournamentThemeRequest? Theme { get; set; }
}

public class UpdateTournamentRequest
{
    public string? Name { get; set; }
    public string? Organizer { get; set; }
    public string? Game { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
    public bool RemoveHeroImage { get; set; } = false;
    public string? CommunityLogoBase64 { get; set; }
    public string? CommunityLogoContentType { get; set; }
    public bool RemoveCommunityLogo { get; set; } = false;
    public string? Rules { get; set; }
    public string? ServerGuid { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public TournamentThemeRequest? Theme { get; set; }
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
    public Instant ScheduledDate { get; set; }
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    public List<string> MapNames { get; set; } = [];
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? Week { get; set; }
}

public class UpdateTournamentMatchRequest
{
    public Instant? ScheduledDate { get; set; }
    public int? Team1Id { get; set; }
    public int? Team2Id { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? Week { get; set; }
    public List<string>? MapNames { get; set; }
}

public class UpdateTournamentMatchMapRequest
{
    public int MapId { get; set; }
    public string? MapName { get; set; }
    public string? RoundId { get; set; }
    public bool UpdateRoundId { get; set; } = false;
    public int? TeamId { get; set; }
}

// Response DTOs
public class TournamentListResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public int MatchCount { get; set; }
    public int TeamCount { get; set; }
    public bool HasHeroImage { get; set; }
    public bool HasCommunityLogo { get; set; }
    public bool HasRules { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public TournamentThemeResponse? Theme { get; set; }
}

public class TournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public List<TournamentTeamResponse> Teams { get; set; } = [];
    public List<MatchWeekGroup> MatchesByWeek { get; set; } = [];
    public bool HasHeroImage { get; set; }
    public bool HasCommunityLogo { get; set; }
    public string? Rules { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public TournamentThemeResponse? Theme { get; set; }
}

public class TournamentTeamResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public List<TournamentTeamPlayerResponse> Players { get; set; } = [];
}

public class TournamentTeamPlayerResponse
{
    public string PlayerName { get; set; } = "";
}

public class TournamentMatchResponse
{
    public int Id { get; set; }
    public Instant ScheduledDate { get; set; }
    public int Team1Id { get; set; }
    public string Team1Name { get; set; } = "";
    public int Team2Id { get; set; }
    public string Team2Name { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? Week { get; set; }
    public Instant CreatedAt { get; set; }
    public List<TournamentMatchMapResponse> Maps { get; set; } = [];
}

public class TournamentMatchMapResponse
{
    public int Id { get; set; }
    public string MapName { get; set; } = "";
    public int MapOrder { get; set; }
    public string? RoundId { get; set; }
    public TournamentRoundResponse? Round { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public TournamentMatchResultResponse? MatchResult { get; set; }
}

public class MatchWeekGroup
{
    public string? Week { get; set; }
    public List<TournamentMatchResponse> Matches { get; set; } = [];
}

public class TournamentMatchResultResponse
{
    public int Id { get; set; }
    public int? Team1Id { get; set; }
    public string? Team1Name { get; set; }
    public int? Team2Id { get; set; }
    public string? Team2Name { get; set; }
    public int? WinningTeamId { get; set; }
    public string? WinningTeamName { get; set; }
    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }
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

// Leaderboard DTOs
public class TournamentTeamRankingResponse
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = "";
    public int RoundsWon { get; set; }
    public int RoundsTied { get; set; }
    public int RoundsLost { get; set; }
    public int TicketDifferential { get; set; }
    public string? Week { get; set; }
}

public class OverrideTeamMappingRequest
{
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
}

public class TournamentMatchResultAdminResponse
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int MatchId { get; set; }
    public int MapId { get; set; }
    public string RoundId { get; set; } = "";
    public string? Week { get; set; }
    public int? Team1Id { get; set; }
    public string? Team1Name { get; set; }
    public int? Team2Id { get; set; }
    public string? Team2Name { get; set; }
    public int? WinningTeamId { get; set; }
    public string? WinningTeamName { get; set; }
    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }
    public Instant UpdatedAt { get; set; }
}

public class RecalculateRankingsResponse
{
    public int TournamentId { get; set; }
    public int TotalRankingsUpdated { get; set; }
    public Instant UpdatedAt { get; set; }
}

public class CleanupOrphanedResultsResponse
{
    public int TournamentId { get; set; }
    public int OrphanedResultsRemoved { get; set; }
    public Instant UpdatedAt { get; set; }
}
