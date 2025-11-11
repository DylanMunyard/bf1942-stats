using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using api.PlayerTracking;
using api.Gamification.Services;
using api.Utils;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Controllers;

[ApiController]
[Route("stats/admin/tournaments")]
public class AdminTournamentController(
    PlayerTrackerDbContext context,
    ILogger<AdminTournamentController> logger,
    IMarkdownSanitizationService markdownSanitizer,
    ITournamentMatchResultService matchResultService,
    ITeamRankingCalculator rankingCalculator) : ControllerBase
{
    /// <summary>
    /// Get the 2 most recent completed matches for a tournament
    /// A match is "completed" when all its maps have at least one match result
    /// </summary>
    private async Task<List<TournamentMatchResponse>> GetLatestMatchesAsync(int tournamentId)
    {
        // Get all matches ordered by scheduled date descending
        var matches = await context.TournamentMatches
            .Where(tm => tm.TournamentId == tournamentId)
            .OrderByDescending(tm => tm.ScheduledDate)
            .Select(tm => new
            {
                tm.Id,
                tm.ScheduledDate,
                tm.Team1Id,
                Team1Name = tm.Team1.Name,
                tm.Team2Id,
                Team2Name = tm.Team2.Name,
                tm.ServerGuid,
                tm.ServerName,
                tm.Week,
                tm.CreatedAt,
                Maps = tm.Maps.Select(m => new
                {
                    m.Id,
                    m.MapName,
                    m.MapOrder,
                    m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                    MatchResultsCount = m.MatchResults.Count(),
                    MatchResults = m.MatchResults.Select(mr => new
                    {
                        mr.Id,
                        mr.Team1Id,
                        Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                        mr.Team2Id,
                        Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                        mr.WinningTeamId,
                        WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                        mr.Team1Tickets,
                        mr.Team2Tickets
                    }).ToList()
                }).ToList()
            })
            .ToListAsync();

        // Filter to completed matches (all maps have at least 1 result) and take 2
        var completedMatches = matches
            .Where(m => m.Maps.Count > 0 && m.Maps.All(map => map.MatchResultsCount > 0))
            .Take(2)
            .ToList();

        // Build response objects
        var matchResponses = new List<TournamentMatchResponse>();

        foreach (var match in completedMatches)
        {
            var matchMapsForThisMatch = new List<TournamentMatchMapResponse>();

            foreach (var map in match.Maps.OrderBy(m => m.MapOrder))
            {
                var matchResultResponses = map.MatchResults.Select(mr =>
                    new TournamentMatchResultResponse
                    {
                        Id = mr.Id,
                        Team1Id = mr.Team1Id,
                        Team1Name = mr.Team1Name,
                        Team2Id = mr.Team2Id,
                        Team2Name = mr.Team2Name,
                        WinningTeamId = mr.WinningTeamId,
                        WinningTeamName = mr.WinningTeamName,
                        Team1Tickets = mr.Team1Tickets,
                        Team2Tickets = mr.Team2Tickets
                    }).ToList();

                matchMapsForThisMatch.Add(new TournamentMatchMapResponse
                {
                    Id = map.Id,
                    MapName = map.MapName,
                    MapOrder = map.MapOrder,
                    TeamId = map.TeamId,
                    TeamName = map.TeamName,
                    MatchResults = matchResultResponses
                });
            }

            matchResponses.Add(new TournamentMatchResponse
            {
                Id = match.Id,
                ScheduledDate = match.ScheduledDate,
                Team1Id = match.Team1Id,
                Team1Name = match.Team1Name,
                Team2Id = match.Team2Id,
                Team2Name = match.Team2Name,
                ServerGuid = match.ServerGuid,
                ServerName = match.ServerName,
                Week = match.Week,
                CreatedAt = match.CreatedAt,
                Maps = matchMapsForThisMatch,
                Files = [],
                Comments = []
            });
        }

        return matchResponses;
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

            var tournaments = await context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.Server)
                .Include(t => t.Theme)
                .Where(t => t.CreatedByUserEmail == userEmail)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var tournamentIds = tournaments.Select(t => t.Id).ToList();

            // Batch load match counts
            var matchCounts = await context.TournamentMatches
                .Where(tm => tournamentIds.Contains(tm.TournamentId))
                .GroupBy(tm => tm.TournamentId)
                .Select(g => new { TournamentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TournamentId, x => x.Count);

            // Batch load team counts
            var teamCounts = await context.TournamentTeams
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
                YouTubeUrl = t.YouTubeUrl,
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
            logger.LogError(ex, "Error getting tournaments");
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

            var tournament = await context.Tournaments
                .Include(t => t.OrganizerPlayer)
                .Include(t => t.Server)
                .Include(t => t.Theme)
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Load teams and their players separately to avoid cartesian product
            var teams = await context.TournamentTeams
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
            var matchResponses = await context.TournamentMatches
                .Where(tm => tm.TournamentId == id)
                .Include(tm => tm.Files)
                .Include(tm => tm.Comments)
                .ThenInclude(c => c.CreatedByUser)
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
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                        MatchResults = m.MatchResults.Select(mr => new TournamentMatchResultResponse
                        {
                            Id = mr.Id,
                            Team1Id = mr.Team1Id,
                            Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                            Team2Id = mr.Team2Id,
                            Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                            WinningTeamId = mr.WinningTeamId,
                            WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                            Team1Tickets = mr.Team1Tickets,
                            Team2Tickets = mr.Team2Tickets
                        }).ToList()
                    }).ToList(),
                    Files = tm.Files.Select(f => new TournamentMatchFileResponse(
                        f.Id,
                        f.Name,
                        f.Url,
                        f.Tags,
                        f.UploadedAt)).ToList(),
                    Comments = tm.Comments.Select(c => new TournamentMatchCommentResponse(
                        c.Id,
                        c.Content,
                        c.CreatedByUserId,
                        c.CreatedByUser != null ? c.CreatedByUser.Email : null,
                        c.CreatedAt,
                        c.UpdatedAt)).ToList()
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

            // Load week dates
            var weekDates = await context.TournamentWeekDates
                .Where(wd => wd.TournamentId == id)
                .OrderBy(wd => wd.StartDate)
                .Select(wd => new TournamentWeekDateResponse(
                    wd.Id,
                    wd.Week,
                    wd.StartDate,
                    wd.EndDate))
                .ToListAsync();

            // Load files
            var files = await context.TournamentFiles
                .Where(f => f.TournamentId == id)
                .OrderByDescending(f => f.UploadedAt)
                .Select(f => new TournamentFileResponse(
                    f.Id,
                    f.Name,
                    f.Url,
                    f.Category,
                    f.UploadedAt))
                .ToListAsync();

            // Get latest matches (2 most recent completed)
            var latestMatches = await GetLatestMatchesAsync(id);

            var response = new TournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                Game = tournament.Game,
                CreatedAt = tournament.CreatedAt,
                AnticipatedRoundCount = tournament.AnticipatedRoundCount,
                Status = tournament.Status,
                GameMode = tournament.GameMode,
                Teams = teams,
                MatchesByWeek = matchesByWeek,
                LatestMatches = latestMatches,
                WeekDates = weekDates,
                Files = files,
                HasHeroImage = tournament.HeroImage != null,
                HasCommunityLogo = tournament.CommunityLogo != null,
                Rules = tournament.Rules,
                ServerGuid = tournament.ServerGuid,
                ServerName = tournament.Server?.Name,
                DiscordUrl = tournament.DiscordUrl,
                ForumUrl = tournament.ForumUrl,
                YouTubeUrl = tournament.YouTubeUrl,
                Theme = themeResponse
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tournament {TournamentId}", id);
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

            var organizer = await context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
            if (organizer == null)
                return BadRequest(new { message = $"Player '{request.Organizer}' not found" });

            if (!string.IsNullOrWhiteSpace(request.ServerGuid))
            {
                var server = await context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
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
                var validationResult = markdownSanitizer.ValidateMarkdown(request.Rules);
                if (!validationResult.IsValid)
                    return BadRequest(new { message = validationResult.Error });

                // Store the raw markdown (safe to store due to validation)
                // The UI will handle rendering the markdown
                sanitizedRules = request.Rules;
            }

            // Validate week dates if provided
            if (request.WeekDates != null)
            {
                foreach (var weekDate in request.WeekDates)
                {
                    if (weekDate.StartDate >= weekDate.EndDate)
                        return BadRequest(new { message = $"Week '{weekDate.Week}': Start date must be before end date" });
                }
            }

            // Validate files if provided
            if (request.Files != null)
            {
                foreach (var file in request.Files)
                {
                    if (string.IsNullOrWhiteSpace(file.Name))
                        return BadRequest(new { message = "File name is required" });

                    if (string.IsNullOrWhiteSpace(file.Url))
                        return BadRequest(new { message = "File URL is required" });
                }
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
                ForumUrl = request.ForumUrl,
                YouTubeUrl = request.YouTubeUrl
            };

            context.Tournaments.Add(tournament);

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
                context.Add(theme);
            }

            // Create week dates if provided
            if (request.WeekDates != null)
            {
                foreach (var weekDate in request.WeekDates)
                {
                    context.TournamentWeekDates.Add(new TournamentWeekDate
                    {
                        Tournament = tournament,
                        Week = weekDate.Week,
                        StartDate = weekDate.StartDate,
                        EndDate = weekDate.EndDate
                    });
                }
            }

            // Create files if provided
            if (request.Files != null)
            {
                foreach (var file in request.Files)
                {
                    context.TournamentFiles.Add(new TournamentFile
                    {
                        Tournament = tournament,
                        Name = file.Name,
                        Url = file.Url,
                        Category = file.Category,
                        UploadedAt = SystemClock.Instance.GetCurrentInstant()
                    });
                }
            }

            await context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetTournament),
                new { id = tournament.Id },
                await GetTournamentDetailOptimizedAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating tournament");
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

            var tournament = await context.Tournaments
                .Include(t => t.Theme)
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
                tournament.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Organizer))
            {
                var organizer = await context.Players.FirstOrDefaultAsync(p => p.Name == request.Organizer);
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
                    var server = await context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
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
                    var validationResult = markdownSanitizer.ValidateMarkdown(request.Rules);
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

            if (request.YouTubeUrl != null)
                tournament.YouTubeUrl = request.YouTubeUrl;

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
                    context.Add(tournament.Theme);
                }
                else
                {
                    // Update existing theme
                    tournament.Theme.BackgroundColour = request.Theme.BackgroundColour;
                    tournament.Theme.TextColour = request.Theme.TextColour;
                    tournament.Theme.AccentColour = request.Theme.AccentColour;
                }
            }

            // Handle status updates
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                var allowedStatuses = new[] { "draft", "registration", "open", "closed" };
                if (!allowedStatuses.Contains(request.Status.ToLower()))
                    return BadRequest(new { message = $"Invalid status. Allowed values: {string.Join(", ", allowedStatuses)}" });

                tournament.Status = request.Status.ToLower();
            }

            // Handle game mode updates
            if (request.GameMode != null)
            {
                // Soft validation - recommend predefined modes but allow any string
                var recommendedModes = new[] { "Conquest", "CTF", "TDM", "Coop" };
                if (!string.IsNullOrWhiteSpace(request.GameMode) && !recommendedModes.Contains(request.GameMode, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Non-standard game mode set for tournament {TournamentId}: {GameMode}", id, request.GameMode);
                }

                tournament.GameMode = string.IsNullOrWhiteSpace(request.GameMode) ? null : request.GameMode;
            }

            // Handle week dates updates (replace all strategy)
            if (request.WeekDates != null)
            {
                // Validate week dates
                foreach (var weekDate in request.WeekDates)
                {
                    if (weekDate.StartDate >= weekDate.EndDate)
                        return BadRequest(new { message = $"Week '{weekDate.Week}': Start date must be before end date" });
                }

                // Remove existing week dates
                var existingWeekDates = await context.TournamentWeekDates
                    .Where(wd => wd.TournamentId == id)
                    .ToListAsync();

                context.TournamentWeekDates.RemoveRange(existingWeekDates);

                // Add new week dates
                foreach (var weekDate in request.WeekDates)
                {
                    context.TournamentWeekDates.Add(new TournamentWeekDate
                    {
                        TournamentId = id,
                        Week = weekDate.Week,
                        StartDate = weekDate.StartDate,
                        EndDate = weekDate.EndDate
                    });
                }
            }

            await context.SaveChangesAsync();

            return Ok(await GetTournamentDetailOptimizedAsync(tournament.Id));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error updating tournament" });
        }
    }

    /// <summary>
    /// Add a file link to a tournament
    /// </summary>
    [HttpPost("{id}/files")]
    [Authorize]
    public async Task<ActionResult<TournamentFileResponse>> CreateTournamentFile(int id, [FromBody] CreateTournamentFileRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "File name is required" });

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { message = "File URL is required" });

            var file = new TournamentFile
            {
                TournamentId = id,
                Name = request.Name,
                Url = request.Url,
                Category = request.Category,
                UploadedAt = SystemClock.Instance.GetCurrentInstant()
            };

            context.TournamentFiles.Add(file);
            await context.SaveChangesAsync();

            var response = new TournamentFileResponse(
                file.Id,
                file.Name,
                file.Url,
                file.Category,
                file.UploadedAt);

            return CreatedAtAction(
                nameof(GetTournament),
                new { id = tournament.Id },
                response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating tournament file for tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error creating tournament file" });
        }
    }

    /// <summary>
    /// Update a tournament file link
    /// </summary>
    [HttpPut("{id}/files/{fileId}")]
    [Authorize]
    public async Task<ActionResult<TournamentFileResponse>> UpdateTournamentFile(
        int id,
        int fileId,
        [FromBody] UpdateTournamentFileRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var file = await context.TournamentFiles
                .Where(f => f.TournamentId == id && f.Id == fileId)
                .FirstOrDefaultAsync();

            if (file == null)
                return NotFound(new { message = "File not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
                file.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Url))
                file.Url = request.Url;

            if (request.Category != null)
                file.Category = request.Category;

            await context.SaveChangesAsync();

            var response = new TournamentFileResponse(
                file.Id,
                file.Name,
                file.Url,
                file.Category,
                file.UploadedAt);

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating tournament file {FileId} for tournament {TournamentId}", fileId, id);
            return StatusCode(500, new { message = "Error updating tournament file" });
        }
    }

    /// <summary>
    /// Delete a tournament file link
    /// </summary>
    [HttpDelete("{id}/files/{fileId}")]
    [Authorize]
    public async Task<IActionResult> DeleteTournamentFile(int id, int fileId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var file = await context.TournamentFiles
                .Where(f => f.TournamentId == id && f.Id == fileId)
                .FirstOrDefaultAsync();

            if (file == null)
                return NotFound(new { message = "File not found" });

            context.TournamentFiles.Remove(file);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "File deleted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting tournament file {FileId} for tournament {TournamentId}", fileId, id);
            return StatusCode(500, new { message = "Error deleting tournament file" });
        }
    }

    /// <summary>
    /// Create a tournament week date
    /// </summary>
    [HttpPost("{id}/weeks")]
    [Authorize]
    public async Task<ActionResult<TournamentWeekDateResponse>> CreateTournamentWeekDate(int id, [FromBody] WeekDateRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (request.StartDate >= request.EndDate)
                return BadRequest(new { message = $"Week '{request.Week}': Start date must be before end date" });

            var weekDate = new TournamentWeekDate
            {
                TournamentId = id,
                Week = request.Week,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };

            context.TournamentWeekDates.Add(weekDate);
            await context.SaveChangesAsync();

            var response = new TournamentWeekDateResponse(
                weekDate.Id,
                weekDate.Week,
                weekDate.StartDate,
                weekDate.EndDate);

            return CreatedAtAction(
                nameof(GetTournament),
                new { id = tournament.Id },
                response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating tournament week date for tournament {TournamentId}", id);
            return StatusCode(500, new { message = "Error creating tournament week date" });
        }
    }

    /// <summary>
    /// Update a tournament week date
    /// </summary>
    [HttpPut("{id}/weeks/{weekId}")]
    [Authorize]
    public async Task<ActionResult<TournamentWeekDateResponse>> UpdateTournamentWeekDate(
        int id,
        int weekId,
        [FromBody] WeekDateRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var weekDate = await context.TournamentWeekDates
                .Where(w => w.TournamentId == id && w.Id == weekId)
                .FirstOrDefaultAsync();

            if (weekDate == null)
                return NotFound(new { message = "Week date not found" });

            if (request.StartDate >= request.EndDate)
                return BadRequest(new { message = $"Week '{request.Week}': Start date must be before end date" });

            weekDate.Week = request.Week;
            weekDate.StartDate = request.StartDate;
            weekDate.EndDate = request.EndDate;

            await context.SaveChangesAsync();

            var response = new TournamentWeekDateResponse(
                weekDate.Id,
                weekDate.Week,
                weekDate.StartDate,
                weekDate.EndDate);

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating tournament week date {WeekId} for tournament {TournamentId}", weekId, id);
            return StatusCode(500, new { message = "Error updating tournament week date" });
        }
    }

    /// <summary>
    /// Delete a tournament week date
    /// </summary>
    [HttpDelete("{id}/weeks/{weekId}")]
    [Authorize]
    public async Task<IActionResult> DeleteTournamentWeekDate(int id, int weekId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var weekDate = await context.TournamentWeekDates
                .Where(w => w.TournamentId == id && w.Id == weekId)
                .FirstOrDefaultAsync();

            if (weekDate == null)
                return NotFound(new { message = "Week date not found" });

            context.TournamentWeekDates.Remove(weekDate);
            await context.SaveChangesAsync();

            return Ok(new { success = true, message = "Week date deleted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting tournament week date {WeekId} for tournament {TournamentId}", weekId, id);
            return StatusCode(500, new { message = "Error deleting tournament week date" });
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

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == id)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            context.Tournaments.Remove(tournament);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting tournament {TournamentId}", id);
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

            var tournament = await context.Tournaments
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
            logger.LogError(ex, "Error getting tournament image {TournamentId}", id);
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

            var tournament = await context.Tournaments
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
            logger.LogError(ex, "Error getting tournament logo {TournamentId}", id);
            return StatusCode(500, new { message = "Error retrieving tournament logo" });
        }
    }

    // Helper methods
    private async Task<User?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var id))
            return null;

        return await context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    /// <summary>
    /// Cleanup orphaned match results - removes MatchResults whose maps no longer exist
    /// This handles cases where cascade delete didn't work properly in previous versions
    /// </summary>
    private async Task<int> CleanupOrphanedMatchResultsAsync(int tournamentId)
    {
        // Find all MatchResults in this tournament that reference non-existent maps
        var orphanedResults = await context.TournamentMatchResults
            .Where(mr => mr.TournamentId == tournamentId)
            .Where(mr => !context.TournamentMatchMaps.Any(m => m.Id == mr.MapId))
            .ToListAsync();

        if (orphanedResults.Count > 0)
        {
            logger.LogWarning(
                "Found {Count} orphaned match results in tournament {TournamentId}. Cleaning up...",
                orphanedResults.Count, tournamentId);

            foreach (var result in orphanedResults)
            {
                logger.LogInformation(
                    "Deleting orphaned match result {ResultId} (referenced non-existent map {MapId})",
                    result.Id, result.MapId);
                context.TournamentMatchResults.Remove(result);
            }

            await context.SaveChangesAsync();
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

    /// <summary>
    /// Helper method to trigger async ranking recalculation for a tournament.
    /// Encapsulates the common pattern used across multiple endpoints.
    /// </summary>
    private void TriggerAsyncRankingRecalculation(int tournamentId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation(
                    "Starting async ranking recalculation for tournament {TournamentId}",
                    tournamentId);
                await rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                logger.LogInformation(
                    "Completed async ranking recalculation for tournament {TournamentId}",
                    tournamentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error during async ranking recalculation for tournament {TournamentId}",
                    tournamentId);
            }
        });
    }

    private async Task<TournamentDetailResponse> GetTournamentDetailOptimizedAsync(int tournamentId)
    {
        var tournament = await context.Tournaments
            .Include(t => t.OrganizerPlayer)
            .Include(t => t.Server)
            .Include(t => t.Theme)
            .FirstAsync(t => t.Id == tournamentId);

        var teams = await context.TournamentTeams
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

        var matches = await context.TournamentMatches
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
                    TeamId = m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                    MatchResults = m.MatchResults.Select(mr => new TournamentMatchResultResponse
                    {
                        Id = mr.Id,
                        Team1Id = mr.Team1Id,
                        Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                        Team2Id = mr.Team2Id,
                        Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                        WinningTeamId = mr.WinningTeamId,
                        WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                        Team1Tickets = mr.Team1Tickets,
                        Team2Tickets = mr.Team2Tickets
                    }).ToList()
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

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Team name is required" });

            // Check if team name already exists in this tournament
            var existingTeam = await context.TournamentTeams
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

            context.TournamentTeams.Add(team);
            await context.SaveChangesAsync();

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
            logger.LogError(ex, "Error creating team for tournament {TournamentId}", tournamentId);
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

            var team = await context.TournamentTeams
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
            logger.LogError(ex, "Error getting team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
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

            var team = await context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (team == null)
                return NotFound(new { message = "Team not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                // Check if new name conflicts with existing team
                var existingTeam = await context.TournamentTeams
                    .Where(tt => tt.TournamentId == tournamentId && tt.Name == request.Name && tt.Id != teamId)
                    .FirstOrDefaultAsync();

                if (existingTeam != null)
                    return BadRequest(new { message = $"Team '{request.Name}' already exists in this tournament" });

                team.Name = request.Name;
            }

            await context.SaveChangesAsync();

            // Return updated team with players
            var response = await context.TournamentTeams
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
            logger.LogError(ex, "Error updating team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
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

            var team = await context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (team == null)
                return NotFound(new { message = "Team not found" });

            var matchesUsingTeam = await context.TournamentMatches
                .Where(tm => tm.Team1Id == teamId || tm.Team2Id == teamId)
                .CountAsync();

            if (matchesUsingTeam > 0)
                return BadRequest(new { message = "Cannot delete team that is used in matches" });

            context.TournamentTeams.Remove(team);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
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

            var teamExists = await context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .AnyAsync();

            if (!teamExists)
                return NotFound(new { message = "Team not found" });

            if (string.IsNullOrWhiteSpace(request.PlayerName))
                return BadRequest(new { message = "Player name is required" });

            var player = await context.Players.FirstOrDefaultAsync(p => p.Name == request.PlayerName);
            if (player == null)
                return BadRequest(new { message = $"Player '{request.PlayerName}' not found" });

            var existingTeamPlayer = await context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == teamId && ttp.PlayerName == request.PlayerName)
                .FirstOrDefaultAsync();

            if (existingTeamPlayer != null)
                return BadRequest(new { message = $"Player '{request.PlayerName}' is already in this team" });

            var playerInOtherTeam = await context.TournamentTeamPlayers
                .Where(ttp => ttp.PlayerName == request.PlayerName && ttp.TournamentTeam.TournamentId == tournamentId)
                .AnyAsync();

            if (playerInOtherTeam)
                return BadRequest(new { message = $"Player '{request.PlayerName}' is already in another team in this tournament" });

            var teamPlayer = new TournamentTeamPlayer
            {
                TournamentTeamId = teamId,
                PlayerName = request.PlayerName
            };

            context.TournamentTeamPlayers.Add(teamPlayer);
            await context.SaveChangesAsync();

            var response = await context.TournamentTeams
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
            logger.LogError(ex, "Error adding player to team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
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

            var teamExists = await context.TournamentTeams
                .Where(tt => tt.Id == teamId && tt.TournamentId == tournamentId && tt.Tournament.CreatedByUserEmail == userEmail)
                .AnyAsync();

            if (!teamExists)
                return NotFound(new { message = "Team not found" });

            var teamPlayer = await context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == teamId && ttp.PlayerName == playerName)
                .FirstOrDefaultAsync();

            if (teamPlayer == null)
                return NotFound(new { message = $"Player '{playerName}' not found in this team" });

            context.TournamentTeamPlayers.Remove(teamPlayer);
            await context.SaveChangesAsync();

            var response = await context.TournamentTeams
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
            logger.LogError(ex, "Error removing player from team {TeamId} for tournament {TournamentId}", teamId, tournamentId);
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

            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (request.Team1Id <= 0 || request.Team2Id <= 0)
                return BadRequest(new { message = "Both Team 1 and Team 2 are required" });

            if (request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team 1 and Team 2 cannot be the same" });

            if (request.Maps == null || request.Maps.Count == 0)
                return BadRequest(new { message = "At least one map is required" });

            // Validate all map names are non-empty
            if (request.Maps.Any(m => string.IsNullOrWhiteSpace(m.MapName)))
                return BadRequest(new { message = "All map names must be non-empty" });

            var teamIds = new[] { request.Team1Id, request.Team2Id };
            var teams = await context.TournamentTeams
                .Where(tt => teamIds.Contains(tt.Id) && tt.TournamentId == tournamentId)
                .Select(tt => new { tt.Id, tt.Name })
                .ToListAsync();

            if (teams.Count != 2)
            {
                var foundTeamIds = teams.Select(t => t.Id).ToList();
                var missingTeamIds = teamIds.Except(foundTeamIds).ToList();
                return BadRequest(new { message = $"Teams with IDs {string.Join(", ", missingTeamIds)} not found in this tournament" });
            }

            // Validate TeamIds in maps if provided
            var mapTeamIds = request.Maps.Where(m => m.TeamId.HasValue).Select(m => m.TeamId.Value).Distinct().ToList();
            if (mapTeamIds.Any())
            {
                var validTeamIds = new[] { request.Team1Id, request.Team2Id };
                var invalidMapTeamIds = mapTeamIds.Where(id => !validTeamIds.Contains(id)).ToList();
                if (invalidMapTeamIds.Any())
                    return BadRequest(new { message = $"Map TeamId(s) {string.Join(", ", invalidMapTeamIds)} are not part of this match's teams" });
            }

            // Validate server if provided
            if (!string.IsNullOrWhiteSpace(request.ServerGuid))
            {
                var serverExists = await context.Servers.AnyAsync(s => s.Guid == request.ServerGuid);
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

            context.TournamentMatches.Add(match);
            await context.SaveChangesAsync();

            // Create map entries
            var maps = request.Maps.Select((mapRequest, index) => new TournamentMatchMap
            {
                MatchId = match.Id,
                MapName = mapRequest.MapName,
                MapOrder = index,
                TeamId = mapRequest.TeamId,
                ImagePath = mapRequest.ImagePath
            }).ToList();

            context.TournamentMatchMaps.AddRange(maps);
            await context.SaveChangesAsync();

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
                    TeamId = m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                    MatchResults = new List<TournamentMatchResultResponse>()
                }).ToList()
            };

            return CreatedAtAction(
                nameof(GetMatch),
                new { tournamentId = tournamentId, matchId = match.Id },
                response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating match for tournament {TournamentId}", tournamentId);
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

            var match = await context.TournamentMatches
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
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                        MatchResults = m.MatchResults.Select(mr => new TournamentMatchResultResponse
                        {
                            Id = mr.Id,
                            Team1Id = mr.Team1Id,
                            Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                            Team2Id = mr.Team2Id,
                            Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                            WinningTeamId = mr.WinningTeamId,
                            WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                            Team1Tickets = mr.Team1Tickets,
                            Team2Tickets = mr.Team2Tickets
                        }).ToList()
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            return Ok(match);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
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

            var match = await context.TournamentMatches
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
                    return BadRequest(new { message = "Team 1 and Team 2 cannot be the same" });

                var validTeams = await context.TournamentTeams
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
                    var serverExists = await context.Servers.AnyAsync(s => s.Guid == request.ServerGuid);
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
            if (request.Maps != null)
            {
                if (request.Maps.Count == 0)
                    return BadRequest(new { message = "At least one map is required" });

                // Validate all map names are non-empty
                if (request.Maps.Any(m => string.IsNullOrWhiteSpace(m.MapName)))
                    return BadRequest(new { message = "All map names must be non-empty" });

                // Validate TeamIds in maps if provided
                var mapTeamIds = request.Maps.Where(m => m.TeamId.HasValue).Select(m => m.TeamId.Value).Distinct().ToList();
                if (mapTeamIds.Any())
                {
                    var validTeamIds = new[] { match.Team1Id, match.Team2Id };
                    var invalidMapTeamIds = mapTeamIds.Where(id => !validTeamIds.Contains(id)).ToList();
                    if (invalidMapTeamIds.Any())
                        return BadRequest(new { message = $"Map TeamId(s) {string.Join(", ", invalidMapTeamIds)} are not part of this match's teams" });
                }

                // Load existing maps WITH their MatchResults to ensure cascade delete works
                var existingMaps = await context.TournamentMatchMaps
                    .Include(m => m.MatchResults)
                    .Where(tmm => tmm.MatchId == matchId)
                    .ToListAsync();

                // Get the list of map names being provided
                var newMapNames = request.Maps.Select(m => m.MapName).ToList();

                // Identify which maps are being removed vs kept
                var mapsToRemove = existingMaps
                    .Where(m => !newMapNames.Contains(m.MapName))
                    .ToList();

                var mapsToKeep = existingMaps
                    .Where(m => newMapNames.Contains(m.MapName))
                    .ToList();

                // Explicitly delete MatchResults for removed maps (ensures proper cleanup)
                foreach (var mapToRemove in mapsToRemove)
                {
                    foreach (var matchResult in mapToRemove.MatchResults)
                    {
                        logger.LogInformation(
                            "Deleting orphaned match result {ResultId} when removing map {MapId} from match {MatchId}",
                            matchResult.Id, mapToRemove.Id, matchId);
                        context.TournamentMatchResults.Remove(matchResult);
                    }
                    context.TournamentMatchMaps.Remove(mapToRemove);
                }

                // Update existing maps that are being kept (preserve MatchResults)
                var newMapOrder = 0;
                foreach (var mapRequest in request.Maps)
                {
                    var existingMap = mapsToKeep.FirstOrDefault(m => m.MapName == mapRequest.MapName);
                    if (existingMap != null)
                    {
                        // Update map order and TeamId
                        if (existingMap.MapOrder != newMapOrder)
                        {
                            logger.LogInformation(
                                "Updating map order for map {MapId} from {OldOrder} to {NewOrder}",
                                existingMap.Id, existingMap.MapOrder, newMapOrder);
                            existingMap.MapOrder = newMapOrder;
                        }

                        if (existingMap.TeamId != mapRequest.TeamId)
                        {
                            logger.LogInformation(
                                "Updating map {MapId} TeamId from {OldTeamId} to {NewTeamId}",
                                existingMap.Id, existingMap.TeamId, mapRequest.TeamId);
                            existingMap.TeamId = mapRequest.TeamId;
                        }

                        if (existingMap.ImagePath != mapRequest.ImagePath)
                        {
                            logger.LogInformation(
                                "Updating map {MapId} ImagePath from {OldImagePath} to {NewImagePath}",
                                existingMap.Id, existingMap.ImagePath, mapRequest.ImagePath);
                            existingMap.ImagePath = mapRequest.ImagePath;
                        }
                    }
                    else
                    {
                        // This is a new map
                        var newMap = new TournamentMatchMap
                        {
                            MatchId = matchId,
                            MapName = mapRequest.MapName,
                            MapOrder = newMapOrder,
                            TeamId = mapRequest.TeamId,
                            ImagePath = mapRequest.ImagePath
                        };
                        logger.LogInformation(
                            "Adding new map '{MapName}' at order {MapOrder} with TeamId {TeamId} and ImagePath {ImagePath} to match {MatchId}",
                            mapRequest.MapName, newMapOrder, mapRequest.TeamId, mapRequest.ImagePath, matchId);
                        context.TournamentMatchMaps.Add(newMap);
                    }
                    newMapOrder++;
                }
            }

            await context.SaveChangesAsync();

            var response = await context.TournamentMatches
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
                        TeamId = m.TeamId,
                        TeamName = m.Team != null ? m.Team.Name : null,
                        ImagePath = m.ImagePath,
                        MatchResults = m.MatchResults.Select(mr => new TournamentMatchResultResponse
                        {
                            Id = mr.Id,
                            Team1Id = mr.Team1Id,
                            Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                            Team2Id = mr.Team2Id,
                            Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                            WinningTeamId = mr.WinningTeamId,
                            WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                            Team1Tickets = mr.Team1Tickets,
                            Team2Tickets = mr.Team2Tickets
                        }).ToList()
                    }).ToList()
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error updating match" });
        }
    }

    /// <summary>
    /// Update a tournament match map metadata (name and team assignment only).
    /// Round linking/unlinking has been moved to match result level APIs.
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
            var match = await context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            var map = await context.TournamentMatchMaps
                .Where(tmm => tmm.Id == mapId && tmm.MatchId == matchId)
                .FirstOrDefaultAsync();

            if (map == null)
                return NotFound(new { message = "Map not found" });

            // Validate TeamId if provided
            if (request.TeamId.HasValue)
            {
                var validTeamIds = new[] { match.Team1Id, match.Team2Id };
                if (!validTeamIds.Contains(request.TeamId.Value))
                    return BadRequest(new { message = $"TeamId {request.TeamId} is not part of this match's teams" });
            }

            // Update map metadata
            if (!string.IsNullOrWhiteSpace(request.MapName))
                map.MapName = request.MapName;

            if (request.TeamId.HasValue)
                map.TeamId = request.TeamId.Value;

            if (request.ImagePath != null)
                map.ImagePath = request.ImagePath;

            if (request.TeamId == null && request.MapName == null && request.ImagePath == null)
                return BadRequest(new { message = "At least one field (MapName, TeamId, or ImagePath) must be updated" });

            await context.SaveChangesAsync();

            var response = await context.TournamentMatchMaps
                .Where(tmm => tmm.Id == mapId)
                .Select(m => new TournamentMatchMapResponse
                {
                    Id = m.Id,
                    MapName = m.MapName,
                    MapOrder = m.MapOrder,
                    TeamId = m.TeamId,
                    TeamName = m.Team != null ? m.Team.Name : null,
                    ImagePath = m.ImagePath,
                    MatchResults = m.MatchResults.Select(mr => new TournamentMatchResultResponse
                    {
                        Id = mr.Id,
                        Team1Id = mr.Team1Id,
                        Team1Name = mr.Team1 != null ? mr.Team1.Name : null,
                        Team2Id = mr.Team2Id,
                        Team2Name = mr.Team2 != null ? mr.Team2.Name : null,
                        WinningTeamId = mr.WinningTeamId,
                        WinningTeamName = mr.WinningTeam != null ? mr.WinningTeam.Name : null,
                        Team1Tickets = mr.Team1Tickets,
                        Team2Tickets = mr.Team2Tickets
                    }).ToList()
                })
                .FirstAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating map {MapId} for match {MatchId}", mapId, matchId);
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

            var match = await context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId && tm.Tournament.CreatedByUserEmail == userEmail)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            context.TournamentMatches.Remove(match);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting match {MatchId} for tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error deleting match" });
        }
    }

    // ===== MANUAL MATCH RESULT ENDPOINTS =====

    /// <summary>
    /// Create a manual match result for a tournament match map.
    /// Optionally link to a round in the same call.
    /// </summary>
    [HttpPost("{tournamentId}/matches/{matchId}/maps/{mapId}/result")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResultAdminResponse>> CreateManualMatchResult(
        int tournamentId,
        int matchId,
        int mapId,
        [FromBody] CreateManualMatchResultRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Verify match belongs to tournament
            var match = await context.TournamentMatches
                .Where(tm => tm.Id == matchId && tm.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found" });

            // Verify map belongs to match
            var map = await context.TournamentMatchMaps
                .Where(m => m.Id == mapId && m.MatchId == matchId)
                .FirstOrDefaultAsync();

            if (map == null)
                return NotFound(new { message = "Map not found" });

            // Validate request - teams are optional but must be both present or both absent
            var hasTeam1 = request.Team1Id.HasValue && request.Team1Id > 0;
            var hasTeam2 = request.Team2Id.HasValue && request.Team2Id > 0;

            if (hasTeam1 != hasTeam2)
                return BadRequest(new { message = "Both Team 1 and Team 2 must be provided together, or neither" });

            if (hasTeam1 && hasTeam2 && request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team 1 and Team 2 cannot be the same" });

            // Validate teams exist in tournament if provided
            if (hasTeam1 && hasTeam2)
            {
                var teamIds = new[] { request.Team1Id!.Value, request.Team2Id!.Value };
                var validTeams = await context.TournamentTeams
                    .Where(tt => teamIds.Contains(tt.Id) && tt.TournamentId == tournamentId)
                    .Select(tt => tt.Id)
                    .ToListAsync();

                var invalidTeams = teamIds.Except(validTeams).ToList();
                if (invalidTeams.Any())
                    return BadRequest(new { message = $"Teams with IDs {string.Join(", ", invalidTeams)} not found in this tournament" });
            }

            // Create the manual result with optional teams
            // Pass null if teams are not provided, otherwise pass the team ID
            var team1Id = hasTeam1 ? request.Team1Id : (int?)null;
            var team2Id = hasTeam2 ? request.Team2Id : (int?)null;

            var (resultId, teamMappingWarning) = await matchResultService.CreateOrUpdateManualMatchResultAsync(
                tournamentId,
                matchId,
                mapId,
                team1Id,
                team2Id,
                request.Team1Tickets,
                request.Team2Tickets,
                request.WinningTeamId,
                request.RoundId);

            logger.LogInformation(
                "Created manual match result {ResultId} for tournament {TournamentId}, match {MatchId}, map {MapId}" +
                (request.RoundId != null ? ", linked to round {RoundId}" : ""),
                resultId, tournamentId, matchId, mapId, request.RoundId);

            if (teamMappingWarning != null)
            {
                logger.LogWarning(
                    "Team mapping warning for match result {ResultId}: {Warning}",
                    resultId, teamMappingWarning);
            }

            // Get the created/updated result before starting background task to avoid DbContext threading issues
            var result = await matchResultService.GetMatchResultAsync(resultId);

            // Trigger ranking recalculation asynchronously
            TriggerAsyncRankingRecalculation(tournamentId);

            var response = new TournamentMatchResultAdminResponse
            {
                Id = result!.Id,
                TournamentId = result.TournamentId,
                MatchId = result.MatchId,
                MapId = result.MapId,
                RoundId = result.RoundId,
                Week = result.Week,
                Team1Id = result.Team1Id,
                Team1Name = result.Team1?.Name,
                Team2Id = result.Team2Id,
                Team2Name = result.Team2?.Name,
                WinningTeamId = result.WinningTeamId,
                WinningTeamName = result.WinningTeam?.Name,
                Team1Tickets = result.Team1Tickets,
                Team2Tickets = result.Team2Tickets,
                TeamMappingWarning = teamMappingWarning,
                UpdatedAt = result.UpdatedAt
            };

            return CreatedAtAction(
                nameof(GetMatch),
                new { tournamentId = tournamentId, matchId = matchId },
                response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid operation when creating manual match result");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating manual match result for tournament {TournamentId}, match {MatchId}", tournamentId, matchId);
            return StatusCode(500, new { message = "Error creating match result" });
        }
    }

    /// <summary>
    /// Update a manual match result (existing result without a linked round)
    /// Allows organizers to update manually entered results
    /// </summary>
    [HttpPut("{tournamentId}/match-results/{resultId}/manual-update")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResultAdminResponse>> UpdateManualMatchResult(
        int tournamentId,
        int resultId,
        [FromBody] UpdateManualMatchResultRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get result and verify it belongs to the tournament
            var result = await context.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.Id == resultId && r.TournamentId == tournamentId);

            if (result == null)
                return NotFound(new { message = "Match result not found" });

            // Validate team assignments
            if (request.Team1Id <= 0 || request.Team2Id <= 0)
                return BadRequest(new { message = "Both Team 1 and Team 2 must be provided" });

            if (request.Team1Id == request.Team2Id)
                return BadRequest(new { message = "Team 1 and Team 2 cannot be the same" });

            // Validate that winning team is one of the two teams (if provided)
            if (request.WinningTeamId.HasValue && request.WinningTeamId > 0)
            {
                if (request.WinningTeamId != request.Team1Id && request.WinningTeamId != request.Team2Id)
                    return BadRequest(new { message = "Winning team must be one of the two teams in the match" });
            }

            logger.LogInformation(
                "Updating manual match result {ResultId} in tournament {TournamentId}",
                resultId, tournamentId);

            // Override team mapping if teams differ from current
            if (result.Team1Id != request.Team1Id || result.Team2Id != request.Team2Id)
            {
                await matchResultService.OverrideTeamMappingAsync(resultId, request.Team1Id, request.Team2Id);
            }

            // Update scores and winning team
            result.Team1Tickets = request.Team1Tickets;
            result.Team2Tickets = request.Team2Tickets;
            result.WinningTeamId = request.WinningTeamId;

            // If it's a draw (equal tickets), clear the winning team
            if (request.Team1Tickets == request.Team2Tickets)
            {
                result.WinningTeamId = null;
            }

            result.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

            context.TournamentMatchResults.Update(result);
            await context.SaveChangesAsync();

            // Get updated result before starting background task to avoid DbContext threading issues
            var updatedResult = await matchResultService.GetMatchResultAsync(resultId);

            // Trigger ranking recalculation asynchronously
            TriggerAsyncRankingRecalculation(tournamentId);
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
            logger.LogError(ex, "Error updating manual match result {ResultId}", resultId);
            return StatusCode(500, new { message = "Error updating match result" });
        }
    }

    /// <summary>
    /// Update the linked round for a match result
    /// </summary>
    [HttpPut("{tournamentId}/match-results/{resultId}/round")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchResultAdminResponse>> UpdateMatchResultRound(
        int tournamentId,
        int resultId,
        [FromBody] UpdateMatchResultRoundRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get result and verify it belongs to the tournament
            var result = await context.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.Id == resultId && r.TournamentId == tournamentId);

            if (result == null)
                return NotFound(new { message = "Match result not found" });

            string? teamMappingWarning = null;

            // Validate round if provided
            if (!string.IsNullOrEmpty(request.RoundId))
            {
                var roundExists = await context.Rounds
                    .AnyAsync(r => r.RoundId == request.RoundId);

                if (!roundExists)
                    return BadRequest(new { message = "Round not found" });

                logger.LogInformation(
                    "Updating round for match result {ResultId} in tournament {TournamentId} to {RoundId}",
                    resultId, tournamentId, request.RoundId);

                // Use the service to link the round - this will auto-detect teams and populate scores
                var (newResultId, warning) = await matchResultService.CreateOrUpdateMatchResultAsync(
                    tournamentId,
                    result.MatchId,
                    result.MapId,
                    request.RoundId);

                if (warning != null)
                {
                    logger.LogWarning("Team mapping warning when updating round: {Warning}", warning);
                    teamMappingWarning = warning;
                }
            }
            else
            {
                // Unlinking the round
                logger.LogInformation(
                    "Unlinking round for match result {ResultId} in tournament {TournamentId}",
                    resultId, tournamentId);

                result.RoundId = null;
                result.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

                context.TournamentMatchResults.Update(result);
                await context.SaveChangesAsync();
            }

            // Return updated result
            var updatedResult = await matchResultService.GetMatchResultAsync(resultId);
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
                TeamMappingWarning = teamMappingWarning,
                UpdatedAt = updatedResult.UpdatedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating round for match result {ResultId}", resultId);
            return StatusCode(500, new { message = "Error updating match result round" });
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
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == User.FindFirstValue(ClaimTypes.Email) && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get rankings from database - Week == week OR (Week == null AND week == null) for cumulative
            var rankings = await context.TournamentTeamRankings
                .Where(r => r.TournamentId == tournamentId && r.Week == week)
                .OrderBy(r => r.Rank)
                .Select(r => new TournamentTeamRankingResponse
                {
                    Rank = r.Rank,
                    TeamId = r.TeamId,
                    TeamName = r.Team != null ? r.Team.Name : $"Team {r.TeamId}",
                    MatchesPlayed = r.MatchesPlayed,
                    Victories = r.Victories,
                    Ties = r.Ties,
                    Losses = r.Losses,
                    RoundsWon = r.RoundsWon,
                    RoundsTied = r.RoundsTied,
                    RoundsLost = r.RoundsLost,
                    TicketsFor = r.TicketsFor,
                    TicketsAgainst = r.TicketsAgainst,
                    TicketDifferential = r.TicketDifferential,
                    Points = r.Points,
                    Week = r.Week
                })
                .ToListAsync();

            return Ok(rankings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting leaderboard for tournament {TournamentId}, week {Week}", tournamentId, week);
            return StatusCode(500, new { message = "Error retrieving leaderboard" });
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
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == User.FindFirstValue(ClaimTypes.Email) && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get result and verify it belongs to the tournament
            var result = await context.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.Id == resultId && r.TournamentId == tournamentId);

            if (result == null)
                return NotFound(new { message = "Match result not found" });

            logger.LogInformation(
                "Deleting match result {ResultId} from tournament {TournamentId}",
                resultId, tournamentId);

            await matchResultService.DeleteMatchResultAsync(resultId);

            // Trigger ranking recalculation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation(
                        "Starting async ranking recalculation after result deletion for tournament {TournamentId}",
                        tournamentId);
                    await rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
                    logger.LogInformation(
                        "Completed async ranking recalculation after result deletion for tournament {TournamentId}",
                        tournamentId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error during async ranking recalculation for tournament {TournamentId}",
                        tournamentId);
                }
            });

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting match result {ResultId}", resultId);
            return StatusCode(500, new { message = "Error deleting match result" });
        }
    }

    // Match Files Endpoints

    /// <summary>
    /// Create a file for a tournament match
    /// </summary>
    [HttpPost("{tournamentId}/matches/{matchId}/files")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchFileResponse>> CreateMatchFile(
        int tournamentId, int matchId, [FromBody] CreateTournamentMatchFileRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Verify match belongs to tournament
            var match = await context.TournamentMatches
                .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId);

            if (match == null)
                return NotFound(new { message = "Match not found" });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "File name is required" });

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { message = "File URL is required" });

            var file = new TournamentMatchFile
            {
                MatchId = matchId,
                Name = request.Name,
                Url = request.Url,
                Tags = request.Tags,
                UploadedAt = SystemClock.Instance.GetCurrentInstant()
            };

            context.TournamentMatchFiles.Add(file);
            await context.SaveChangesAsync();

            return Ok(new TournamentMatchFileResponse(
                file.Id,
                file.Name,
                file.Url,
                file.Tags,
                file.UploadedAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating match file");
            return StatusCode(500, new { message = "Error creating match file" });
        }
    }

    /// <summary>
    /// Update a file for a tournament match
    /// </summary>
    [HttpPut("{tournamentId}/matches/{matchId}/files/{fileId}")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchFileResponse>> UpdateMatchFile(
        int tournamentId, int matchId, int fileId, [FromBody] UpdateTournamentMatchFileRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get the file and verify it belongs to the match
            var file = await context.TournamentMatchFiles
                .Include(f => f.Match)
                .FirstOrDefaultAsync(f => f.Id == fileId && f.MatchId == matchId);

            if (file == null)
                return NotFound(new { message = "File not found" });

            if (!string.IsNullOrWhiteSpace(request.Name))
                file.Name = request.Name;

            if (!string.IsNullOrWhiteSpace(request.Url))
                file.Url = request.Url;

            if (request.Tags != null)
                file.Tags = request.Tags;

            await context.SaveChangesAsync();

            return Ok(new TournamentMatchFileResponse(
                file.Id,
                file.Name,
                file.Url,
                file.Tags,
                file.UploadedAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating match file");
            return StatusCode(500, new { message = "Error updating match file" });
        }
    }

    /// <summary>
    /// Delete a file from a tournament match
    /// </summary>
    [HttpDelete("{tournamentId}/matches/{matchId}/files/{fileId}")]
    [Authorize]
    public async Task<IActionResult> DeleteMatchFile(int tournamentId, int matchId, int fileId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get the file and verify it belongs to the match
            var file = await context.TournamentMatchFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.MatchId == matchId);

            if (file == null)
                return NotFound(new { message = "File not found" });

            context.TournamentMatchFiles.Remove(file);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting match file");
            return StatusCode(500, new { message = "Error deleting match file" });
        }
    }

    // Match Comments Endpoints

    /// <summary>
    /// Create a comment for a tournament match
    /// </summary>
    [HttpPost("{tournamentId}/matches/{matchId}/comments")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchCommentResponse>> CreateMatchComment(
        int tournamentId, int matchId, [FromBody] CreateTournamentMatchCommentRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Get current user
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Verify match belongs to tournament
            var match = await context.TournamentMatches
                .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId);

            if (match == null)
                return NotFound(new { message = "Match not found" });

            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { message = "Comment content is required" });

            var now = SystemClock.Instance.GetCurrentInstant();
            var comment = new TournamentMatchComment
            {
                MatchId = matchId,
                Content = request.Content,
                CreatedByUserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.TournamentMatchComments.Add(comment);
            await context.SaveChangesAsync();

            return Ok(new TournamentMatchCommentResponse(
                comment.Id,
                comment.Content,
                comment.CreatedByUserId,
                userEmail,
                comment.CreatedAt,
                comment.UpdatedAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating match comment");
            return StatusCode(500, new { message = "Error creating match comment" });
        }
    }

    /// <summary>
    /// Update a comment on a tournament match (organizer only)
    /// </summary>
    [HttpPut("{tournamentId}/matches/{matchId}/comments/{commentId}")]
    [Authorize]
    public async Task<ActionResult<TournamentMatchCommentResponse>> UpdateMatchComment(
        int tournamentId, int matchId, int commentId, [FromBody] UpdateTournamentMatchCommentRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            // Verify tournament belongs to user (organizer only)
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get the comment and verify it belongs to the match
            var comment = await context.TournamentMatchComments
                .Include(c => c.CreatedByUser)
                .FirstOrDefaultAsync(c => c.Id == commentId && c.MatchId == matchId);

            if (comment == null)
                return NotFound(new { message = "Comment not found" });

            // Only organizer can edit comments
            if (comment.CreatedByUserId != user.Id)
                return Forbid();

            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { message = "Comment content is required" });

            comment.Content = request.Content;
            comment.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

            await context.SaveChangesAsync();

            return Ok(new TournamentMatchCommentResponse(
                comment.Id,
                comment.Content,
                comment.CreatedByUserId,
                comment.CreatedByUser?.Email,
                comment.CreatedAt,
                comment.UpdatedAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating match comment");
            return StatusCode(500, new { message = "Error updating match comment" });
        }
    }

    /// <summary>
    /// Delete a comment from a tournament match (organizer only)
    /// </summary>
    [HttpDelete("{tournamentId}/matches/{matchId}/comments/{commentId}")]
    [Authorize]
    public async Task<IActionResult> DeleteMatchComment(int tournamentId, int matchId, int commentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            // Verify tournament belongs to user (organizer only)
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get the comment and verify it belongs to the match
            var comment = await context.TournamentMatchComments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.MatchId == matchId);

            if (comment == null)
                return NotFound(new { message = "Comment not found" });

            // Only organizer can delete comments
            if (comment.CreatedByUserId != user.Id)
                return Forbid();

            context.TournamentMatchComments.Remove(comment);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting match comment");
            return StatusCode(500, new { message = "Error deleting match comment" });
        }
    }


    /// <summary>
    /// Get files and comments for a specific match (authenticated organizer only)
    /// Returns all files and comments for a match in a single call
    /// </summary>
    [HttpGet("{tournamentId}/matches/{matchId}/files-and-comments")]
    [Authorize]
    public async Task<ActionResult<MatchFilesAndCommentsResponse>> GetMatchFilesAndComments(
        int tournamentId,
        int matchId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Verify match belongs to tournament
            var match = await context.TournamentMatches
                .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId);

            if (match == null)
                return NotFound(new { message = "Match not found" });

            // Load files and comments in parallel
            var filesTask = context.TournamentMatchFiles
                .Where(f => f.MatchId == matchId)
                .OrderByDescending(f => f.UploadedAt)
                .Select(f => new TournamentMatchFileResponse(
                    f.Id,
                    f.Name,
                    f.Url,
                    f.Tags,
                    f.UploadedAt))
                .ToListAsync();

            var commentsTask = context.TournamentMatchComments
                .Where(c => c.MatchId == matchId)
                .Include(c => c.CreatedByUser)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new TournamentMatchCommentResponse(
                    c.Id,
                    c.Content,
                    c.CreatedByUserId,
                    c.CreatedByUser != null ? c.CreatedByUser.Email : null,
                    c.CreatedAt,
                    c.UpdatedAt))
                .ToListAsync();

            await Task.WhenAll(filesTask, commentsTask);

            var files = await filesTask;
            var comments = await commentsTask;

            var response = new MatchFilesAndCommentsResponse
            {
                TournamentId = tournamentId,
                MatchId = matchId,
                Files = files,
                Comments = comments
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting files and comments for match {MatchId} in tournament {TournamentId}", matchId, tournamentId);
            return StatusCode(500, new { message = "Error retrieving match files and comments" });
        }
    }

    /// <summary>
    /// Manually trigger ranking recalculation for a tournament with flexible week filtering
    /// Allows admins to recalculate leaderboards for:
    /// - All weeks (default - no request body)
    /// - A single week (specify Week parameter)
    /// - From a specific week onwards (specify FromWeek parameter)
    /// </summary>
    [HttpPost("{tournamentId}/leaderboard/recalculate")]
    [Authorize]
    public async Task<ActionResult<RecalculateRankingsAdvancedResponse>> RecalculateRankings(
        int tournamentId,
        [FromBody] RecalculateRankingsAdvancedRequest? request = null)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found in token" });

            // Verify tournament belongs to user
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            request ??= new RecalculateRankingsAdvancedRequest();

            logger.LogInformation(
                "Enhanced ranking recalculation triggered for tournament {TournamentId} with request: {Request}",
                tournamentId, System.Text.Json.JsonSerializer.Serialize(request));

            // Load team names mapping
            var teamNamesMap = await context.TournamentTeams
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            var weeksToRecalculate = new List<string?>();
            var (allWeeks, cumulativeUpdated) = await GetAllWeeksAndRecalculateCumulativeAsync(tournamentId);

            if (!string.IsNullOrWhiteSpace(request.Week))
            {
                // Single week recalculation
                weeksToRecalculate.Add(request.Week);
                logger.LogInformation(
                    "Recalculating rankings for specific week: {Week}",
                    request.Week);
            }
            else if (!string.IsNullOrWhiteSpace(request.FromWeek))
            {
                // From week onwards recalculation
                var weeksFromStartingPoint = allWeeks
                    .SkipWhile(w => w != request.FromWeek)
                    .ToList();

                if (!weeksFromStartingPoint.Any())
                {
                    logger.LogWarning(
                        "Requested FromWeek '{FromWeek}' not found in tournament {TournamentId}. Available weeks: {AvailableWeeks}",
                        request.FromWeek, tournamentId, string.Join(", ", allWeeks));
                    return BadRequest(new { message = $"Week '{request.FromWeek}' not found in tournament" });
                }

                weeksToRecalculate.AddRange(weeksFromStartingPoint);
                logger.LogInformation(
                    "Recalculating rankings from week '{FromWeek}' onwards. Weeks to recalculate: {Weeks}",
                    request.FromWeek, string.Join(", ", weeksToRecalculate));
            }
            else
            {
                // All weeks recalculation (default)
                weeksToRecalculate.AddRange(allWeeks);
                logger.LogInformation(
                    "Recalculating rankings for all weeks: {Weeks}",
                    string.Join(", ", allWeeks));
            }

            // Recalculate rankings for specified weeks
            var weeklyRankingsUpdated = 0;
            var updatedRankingsByWeek = new Dictionary<string, List<TournamentTeamRankingResponse>>();
            List<TournamentTeamRankingResponse> cumulativeRankings = [];

            // Helper function to map rankings to response objects
            TournamentTeamRankingResponse MapRankingToResponse(TournamentTeamRanking ranking)
            {
                var teamName = teamNamesMap.TryGetValue(ranking.TeamId, out var name)
                    ? name
                    : $"Team {ranking.TeamId}";
                return new TournamentTeamRankingResponse
                {
                    Rank = ranking.Rank,
                    TeamId = ranking.TeamId,
                    TeamName = teamName,
                    MatchesPlayed = ranking.MatchesPlayed,
                    Victories = ranking.Victories,
                    Ties = ranking.Ties,
                    Losses = ranking.Losses,
                    RoundsWon = ranking.RoundsWon,
                    RoundsTied = ranking.RoundsTied,
                    RoundsLost = ranking.RoundsLost,
                    TicketsFor = ranking.TicketsFor,
                    TicketsAgainst = ranking.TicketsAgainst,
                    TicketDifferential = ranking.TicketDifferential,
                    Points = ranking.Points,
                    Week = ranking.Week
                };
            }

            // Get cumulative rankings
            var cumulativeRankingsList = await rankingCalculator.CalculateRankingsAsync(tournamentId, null);

            // Save cumulative rankings to database
            var oldCumulativeRankings = await context.TournamentTeamRankings
                .Where(r => r.TournamentId == tournamentId && r.Week == null)
                .ToListAsync();
            context.TournamentTeamRankings.RemoveRange(oldCumulativeRankings);
            await context.TournamentTeamRankings.AddRangeAsync(cumulativeRankingsList);
            await context.SaveChangesAsync();

            cumulativeRankings = cumulativeRankingsList
                .OrderBy(r => r.Rank)
                .Select(MapRankingToResponse)
                .ToList();

            // Recalculate specific weeks
            foreach (var week in weeksToRecalculate)
            {
                if (string.IsNullOrWhiteSpace(week))
                    continue;

                var rankings = await rankingCalculator.CalculateRankingsAsync(tournamentId, week);
                weeklyRankingsUpdated += rankings.Count;

                // Save weekly rankings to database
                var oldWeeklyRankings = await context.TournamentTeamRankings
                    .Where(r => r.TournamentId == tournamentId && r.Week == week)
                    .ToListAsync();
                context.TournamentTeamRankings.RemoveRange(oldWeeklyRankings);
                await context.TournamentTeamRankings.AddRangeAsync(rankings);
                await context.SaveChangesAsync();

                var response = rankings
                    .OrderBy(r => r.Rank)
                    .Select(MapRankingToResponse)
                    .ToList();

                updatedRankingsByWeek[week] = response;

                logger.LogInformation(
                    "Updated {Count} rankings for tournament {TournamentId}, week \"{Week}\"",
                    rankings.Count, tournamentId, week);
            }

            var result = new RecalculateRankingsAdvancedResponse
            {
                TournamentId = tournamentId,
                TotalRankingsUpdated = weeklyRankingsUpdated + cumulativeUpdated,
                WeeksRecalculated = weeksToRecalculate.Where(w => !string.IsNullOrWhiteSpace(w)).ToList()!,
                CumulativeRankings = cumulativeRankings,
                RankingsByWeek = updatedRankingsByWeek,
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating rankings for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error recalculating rankings" });
        }
    }

    /// <summary>
    /// Helper method to get all distinct weeks and recalculate cumulative rankings
    /// </summary>
    private async Task<(List<string?>, int)> GetAllWeeksAndRecalculateCumulativeAsync(int tournamentId)
    {
        // Get all distinct weeks from match results
        var weeks = await context.TournamentMatchResults
            .Where(mr => mr.TournamentId == tournamentId && mr.Week != null)
            .Select(mr => mr.Week)
            .Distinct()
            .OrderBy(w => w)
            .ToListAsync();

        // Always recalculate cumulative rankings (week = null)
        var cumulativeRankings = await rankingCalculator.CalculateRankingsAsync(tournamentId, null);

        logger.LogInformation(
            "Recalculated cumulative rankings for tournament {TournamentId}: {Count} rankings",
            tournamentId, cumulativeRankings.Count);

        return (weeks, cumulativeRankings.Count);
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
            var tournament = await context.Tournaments
                .Where(t => t.CreatedByUserEmail == userEmail && t.Id == tournamentId)
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            logger.LogInformation(
                "Manual cleanup of orphaned match results triggered for tournament {TournamentId}",
                tournamentId);

            var orphanedCount = await CleanupOrphanedMatchResultsAsync(tournamentId);

            if (orphanedCount > 0)
            {
                logger.LogInformation(
                    "Cleaned up {Count} orphaned match results from tournament {TournamentId}. Recalculating rankings...",
                    orphanedCount, tournamentId);

                // Recalculate rankings after cleanup
                await rankingCalculator.RecalculateAllRankingsAsync(tournamentId);
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
            logger.LogError(ex, "Error cleaning up orphaned results for tournament {TournamentId}", tournamentId);
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
    public string? YouTubeUrl { get; set; }
    public TournamentThemeRequest? Theme { get; set; }
    public List<WeekDateRequest>? WeekDates { get; set; }
    public List<CreateTournamentFileRequest>? Files { get; set; }
}

public class UpdateTournamentRequest
{
    public string? Name { get; set; }
    public string? Organizer { get; set; }
    public string? Game { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public string? Status { get; set; } // draft, registration, open, closed
    public string? GameMode { get; set; } // Conquest, CTF, etc.
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
    public string? YouTubeUrl { get; set; }
    public TournamentThemeRequest? Theme { get; set; }
    public List<WeekDateRequest>? WeekDates { get; set; } // Replace all week dates
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
public class CreateTournamentMapRequest
{
    public string MapName { get; set; } = "";
    public int? TeamId { get; set; }
    public string? ImagePath { get; set; }
}

public class CreateTournamentMatchRequest
{
    public Instant ScheduledDate { get; set; }
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    public List<CreateTournamentMapRequest> Maps { get; set; } = [];
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
    public List<CreateTournamentMapRequest>? Maps { get; set; }
}

public class UpdateTournamentMatchMapRequest
{
    public int MapId { get; set; }
    public string? MapName { get; set; }
    public int? TeamId { get; set; }
    public string? ImagePath { get; set; } // e.g., "golden-gun/map1.png"
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
    public string? YouTubeUrl { get; set; }
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
    public string Status { get; set; } = ""; // draft, registration, open, closed
    public string? GameMode { get; set; } // Conquest, CTF, etc.
    public List<TournamentTeamResponse> Teams { get; set; } = [];
    public List<MatchWeekGroup> MatchesByWeek { get; set; } = [];
    public List<TournamentMatchResponse> LatestMatches { get; set; } = []; // 2 most recent completed matches
    public List<TournamentWeekDateResponse> WeekDates { get; set; } = [];
    public List<TournamentFileResponse> Files { get; set; } = [];
    public bool HasHeroImage { get; set; }
    public bool HasCommunityLogo { get; set; }
    public string? Rules { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public string? YouTubeUrl { get; set; }
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
    public List<TournamentMatchFileResponse> Files { get; set; } = [];
    public List<TournamentMatchCommentResponse> Comments { get; set; } = [];
}

public class TournamentMatchMapResponse
{
    public int Id { get; set; }
    public string MapName { get; set; } = "";
    public int MapOrder { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ImagePath { get; set; } // e.g., "golden-gun/map1.png"
    public List<TournamentMatchResultResponse> MatchResults { get; set; } = [];
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
    public string? Week { get; set; }

    // Match-level statistics
    public int MatchesPlayed { get; set; }
    public int Victories { get; set; }
    public int Ties { get; set; }
    public int Losses { get; set; }

    // Round-level statistics
    public int RoundsWon { get; set; }
    public int RoundsTied { get; set; }
    public int RoundsLost { get; set; }

    // Ticket statistics
    public int TicketsFor { get; set; }
    public int TicketsAgainst { get; set; }
    public int TicketDifferential { get; set; }

    // Points (primary ranking metric)
    public int Points { get; set; }
}

public class CreateManualMatchResultRequest
{
    public int MapId { get; set; }
    /// <summary>
    /// Optional: Team 1 ID. If not provided, can be set later via manual-update endpoint.
    /// </summary>
    public int? Team1Id { get; set; }
    /// <summary>
    /// Optional: Team 2 ID. If not provided, can be set later via manual-update endpoint.
    /// </summary>
    public int? Team2Id { get; set; }
    /// <summary>
    /// Optional: Team 1 score. If not provided, will be pulled from the linked round (if provided).
    /// </summary>
    public int? Team1Tickets { get; set; }
    /// <summary>
    /// Optional: Team 2 score. If not provided, will be pulled from the linked round (if provided).
    /// </summary>
    public int? Team2Tickets { get; set; }
    public int? WinningTeamId { get; set; }
    /// <summary>
    /// Optional: Link the result to a round upon creation.
    /// If provided, teams and tickets will be auto-detected from the round data (if available).
    /// </summary>
    public string? RoundId { get; set; }
}

public class UpdateManualMatchResultRequest
{
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }
    public int? WinningTeamId { get; set; }
}

public class UpdateMatchResultRoundRequest
{
    public string? RoundId { get; set; }
}

public class TournamentMatchResultAdminResponse
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int MatchId { get; set; }
    public int MapId { get; set; }
    public string? RoundId { get; set; } // Now nullable - can be manually entered without a round
    public string? Week { get; set; }
    public int? Team1Id { get; set; }
    public string? Team1Name { get; set; }
    public int? Team2Id { get; set; }
    public string? Team2Name { get; set; }
    public int? WinningTeamId { get; set; }
    public string? WinningTeamName { get; set; }
    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }
    public string? TeamMappingWarning { get; set; }
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


public class RecalculateRankingsAdvancedRequest
{
    /// <summary>
    /// Optional: Specific week to recalculate.
    /// If provided, only this week's rankings will be recalculated.
    /// If not provided, defaults to recalculating all weeks.
    /// </summary>
    public string? Week { get; set; }

    /// <summary>
    /// Optional: Starting week for "from week onwards" recalculation.
    /// If provided, all weeks from this week onwards will be recalculated.
    /// Takes precedence over Week if both are provided.
    /// If neither Week nor FromWeek are provided, all weeks are recalculated.
    /// </summary>
    public string? FromWeek { get; set; }
}

public class RecalculateRankingsAdvancedResponse
{
    /// <summary>
    /// The tournament ID
    /// </summary>
    public int TournamentId { get; set; }

    /// <summary>
    /// Total number of ranking records updated (across all weeks and cumulative)
    /// </summary>
    public int TotalRankingsUpdated { get; set; }

    /// <summary>
    /// List of weeks that were recalculated
    /// </summary>
    public List<string> WeeksRecalculated { get; set; } = [];

    /// <summary>
    /// Cumulative rankings across all weeks
    /// </summary>
    public List<TournamentTeamRankingResponse> CumulativeRankings { get; set; } = [];

    /// <summary>
    /// Rankings grouped by week, showing the calculated standings for each specific week
    /// </summary>
    public Dictionary<string, List<TournamentTeamRankingResponse>> RankingsByWeek { get; set; } = [];

    /// <summary>
    /// Timestamp of when the recalculation was performed
    /// </summary>
    public Instant UpdatedAt { get; set; }
}

public record TournamentWeekDateResponse(
    int Id,
    string? Week,
    LocalDate StartDate,
    LocalDate EndDate);

public record TournamentFileResponse(
    int Id,
    string Name,
    string Url,
    string? Category,
    Instant UploadedAt);

public record WeekDateRequest(
    string? Week,
    LocalDate StartDate,
    LocalDate EndDate);

public record CreateTournamentFileRequest(
    string Name,
    string Url,
    string? Category);

public record UpdateTournamentFileRequest(
    string? Name,
    string? Url,
    string? Category);

// Match Files DTOs
public record CreateTournamentMatchFileRequest(
    string Name,
    string Url,
    string? Tags);

public record UpdateTournamentMatchFileRequest(
    string? Name,
    string? Url,
    string? Tags);

public record TournamentMatchFileResponse(
    int Id,
    string Name,
    string Url,
    string? Tags,
    Instant UploadedAt);

// Match Comments DTOs
public record CreateTournamentMatchCommentRequest(
    string Content);

public record UpdateTournamentMatchCommentRequest(
    string Content);

public record TournamentMatchCommentResponse(
    int Id,
    string Content,
    int CreatedByUserId,
    string? CreatedByUserEmail,
    Instant CreatedAt,
    Instant UpdatedAt);

// Combined response for fetching files and comments together
public class MatchFilesAndCommentsResponse
{
    public int TournamentId { get; set; }
    public int MatchId { get; set; }
    public List<TournamentMatchFileResponse> Files { get; set; } = [];
    public List<TournamentMatchCommentResponse> Comments { get; set; } = [];
}
