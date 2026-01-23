using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using api.PlayerTracking;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Controllers;

[ApiController]
[Route("stats/tournaments")]
public class PublicTournamentController(
    PlayerTrackerDbContext context,
    ILogger<PublicTournamentController> logger) : ControllerBase
{
    /// <summary>
    /// Extract the 2 most recent completed matches from already-loaded data
    /// A match is "completed" when all its maps have at least one match result
    /// </summary>
    private List<PublicTournamentMatchResponse> GetLatestMatches(
        List<PublicTournamentMatchResponse> allMatches)
    {
        return allMatches
            .Where(m => m.Maps.Count > 0 && m.Maps.All(map => map.MatchResults.Count > 0))
            .OrderByDescending(m => m.ScheduledDate)
            .Take(2)
            .ToList();
    }

    /// <summary>
    /// Load all teams and their players for a tournament
    /// </summary>
    private async Task<List<PublicTournamentTeamResponse>> GetTeamsAsync(int tournamentId)
    {
        // Load teams for this tournament
        var teams = await context.TournamentTeams
            .Where(tt => tt.TournamentId == tournamentId)
            .Select(tt => new { tt.Id, tt.Name, tt.Tag, tt.CreatedAt })
            .ToListAsync();

        var teamIds = teams.Select(t => t.Id).ToList();

        // Batch load all team players with leader info
        var teamPlayers = teamIds.Count > 0
            ? await context.TournamentTeamPlayers
                .Where(ttp => teamIds.Contains(ttp.TournamentTeamId))
                .Select(ttp => new { ttp.TournamentTeamId, ttp.PlayerName, ttp.IsTeamLeader })
                .ToListAsync()
            : [];

        var teamPlayersLookup = teamPlayers
            .GroupBy(tp => tp.TournamentTeamId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.IsTeamLeader)
                      .Select(x => new { x.PlayerName, x.IsTeamLeader })
                      .ToList());

        // Build team responses
        return teams.Select(t =>
        {
            var players = teamPlayersLookup.GetValueOrDefault(t.Id, []);
            var leaderPlayerName = players.FirstOrDefault(p => p.IsTeamLeader)?.PlayerName;

            return new PublicTournamentTeamResponse
            {
                Id = t.Id,
                Name = t.Name,
                Tag = t.Tag,
                CreatedAt = t.CreatedAt,
                LeaderPlayerName = leaderPlayerName,
                Players = players
                    .Select(p => new PublicTournamentTeamPlayerResponse
                    {
                        PlayerName = p.PlayerName,
                        IsLeader = p.IsTeamLeader
                    })
                    .ToList()
            };
        }).ToList();
    }

    /// <summary>
    /// Load all matches and their maps with results for a tournament
    /// </summary>
    private async Task<(List<PublicTournamentMatchResponse> AllMatches, List<PublicMatchWeekGroup> ByWeek)> GetMatchesAsync(int tournamentId)
    {
        // Load matches for this tournament
        var matches = await context.TournamentMatches
            .Where(tm => tm.TournamentId == tournamentId)
            .Select(tm => new
            {
                tm.Id,
                tm.ScheduledDate,
                tm.Team1Id,
                tm.Team2Id,
                Team1Name = tm.Team1 != null ? tm.Team1.Name : null,
                Team2Name = tm.Team2 != null ? tm.Team2.Name : null,
                tm.ServerGuid,
                tm.ServerName,
                tm.Week,
                tm.CreatedAt
            })
            .ToListAsync();

        var matchIds = matches.Select(m => m.Id).ToList();

        // Batch load all match maps with their match results
        var matchMaps = matchIds.Count > 0
            ? await context.TournamentMatchMaps
                .Where(tmm => matchIds.Contains(tmm.MatchId))
                .Select(tmm => new
                {
                    tmm.Id,
                    tmm.MatchId,
                    tmm.MapName,
                    tmm.MapOrder,
                    tmm.TeamId,
                    TeamName = tmm.Team != null ? tmm.Team.Name : null,
                    tmm.ImagePath,
                    MatchResults = tmm.MatchResults.Select(mr => new
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
                })
                .ToListAsync()
            : [];

        var matchMapsLookup = matchMaps
            .GroupBy(mm => mm.MatchId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.MapOrder).ToList());

        // Build match responses
        var matchResponses = new List<PublicTournamentMatchResponse>();

        foreach (var match in matches.OrderBy(m => m.ScheduledDate))
        {
            var matchMapsForThisMatch = new List<PublicTournamentMatchMapResponse>();

            if (matchMapsLookup.TryGetValue(match.Id, out var mapsForMatch))
            {
                foreach (var map in mapsForMatch)
                {
                    var matchResultResponses = map.MatchResults.Select(mr =>
                        new PublicTournamentMatchResultResponse
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

                    matchMapsForThisMatch.Add(new PublicTournamentMatchMapResponse
                    {
                        Id = map.Id,
                        MapName = map.MapName,
                        MapOrder = map.MapOrder,
                        TeamId = map.TeamId,
                        TeamName = map.TeamName,
                        ImagePath = map.ImagePath,
                        MatchResults = matchResultResponses
                    });
                }
            }

            matchResponses.Add(new PublicTournamentMatchResponse
            {
                Id = match.Id,
                ScheduledDate = match.ScheduledDate,
                Team1Name = match.Team1Name,
                Team2Name = match.Team2Name,
                ServerGuid = match.ServerGuid,
                ServerName = match.ServerName,
                Week = match.Week,
                CreatedAt = match.CreatedAt,
                Maps = matchMapsForThisMatch
            });
        }

        // Group matches by week
        var matchesByWeek = matchResponses
            .GroupBy(m => m.Week)
            .OrderBy(g => g.Key)
            .Select(g => new PublicMatchWeekGroup
            {
                Week = g.Key,
                Matches = g.ToList()
            })
            .ToList();

        return (matchResponses, matchesByWeek);
    }

    /// <summary>
    /// Load tournament files
    /// </summary>
    private async Task<List<PublicTournamentFileResponse>> GetFilesAsync(int tournamentId)
    {
        return await context.TournamentFiles
            .Where(f => f.TournamentId == tournamentId)
            .Select(f => new PublicTournamentFileResponse(
                f.Id,
                f.Name,
                f.Url,
                f.Category,
                f.UploadedAt))
            .ToListAsync();
    }

    /// <summary>
    /// Get tournament details by ID or name (public, no auth required)
    /// </summary>
    [HttpGet("{idOrName}")]
    public async Task<ActionResult<PublicTournamentDetailResponse>> GetTournament(string idOrName)
    {
        try
        {
            Tournament? tournament;

            // Try to parse as integer first (ID lookup)
            if (int.TryParse(idOrName, out int id))
            {
                tournament = await context.Tournaments
                    .Include(t => t.Server)
                    .Include(t => t.Theme)
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                // If not a number, search by name
                tournament = await context.Tournaments
                    .Include(t => t.Server)
                    .Include(t => t.Theme)
                    .FirstOrDefaultAsync(t => t.Name == idOrName);
            }

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Filter out draft tournaments from public view
            if (tournament.Status == "draft")
                return NotFound(new { message = "Tournament not found" });

            var tournamentId = tournament.Id;

            // Parallelize independent DB operations: teams, matches, and files
            var teamsTask = GetTeamsAsync(tournamentId);
            var matchesTask = GetMatchesAsync(tournamentId);
            var filesTask = GetFilesAsync(tournamentId);

            await Task.WhenAll(teamsTask, matchesTask, filesTask);

            var teamResponses = await teamsTask;
            var (allMatches, matchesByWeek) = await matchesTask;
            var files = await filesTask;

            // Extract latest matches from already-loaded data (no additional DB call)
            var latestMatches = GetLatestMatches(allMatches);

            var themeResponse = tournament.Theme != null ? new PublicTournamentThemeResponse
            {
                BackgroundColour = tournament.Theme.BackgroundColour,
                TextColour = tournament.Theme.TextColour,
                AccentColour = tournament.Theme.AccentColour
            } : null;

            var response = new PublicTournamentDetailResponse
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Organizer = tournament.Organizer,
                Game = tournament.Game,
                CreatedAt = tournament.CreatedAt,
                AnticipatedRoundCount = tournament.AnticipatedRoundCount,
                Status = tournament.Status,
                GameMode = tournament.GameMode,
                Teams = teamResponses,
                MatchesByWeek = matchesByWeek,
                LatestMatches = latestMatches,
                Files = files,
                HasHeroImage = tournament.HeroImage != null,
                HasCommunityLogo = tournament.CommunityLogo != null,
                Rules = tournament.Rules,
                RegistrationRules = tournament.RegistrationRules,
                ServerGuid = tournament.ServerGuid,
                ServerName = tournament.Server?.Name,
                DiscordUrl = tournament.DiscordUrl,
                ForumUrl = tournament.ForumUrl,
                YouTubeUrl = tournament.YouTubeUrl,
                TwitchUrl = tournament.TwitchUrl,
                Theme = themeResponse
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting public tournament {TournamentId}", idOrName);
            return StatusCode(500, new { message = "Error retrieving tournament" });
        }
    }

    /// <summary>
    /// Get tournament hero image (public, no auth required)
    /// </summary>
    [HttpGet("{id}/image")]
    public async Task<IActionResult> GetTournamentImage(int id)
    {
        try
        {
            var tournament = await context.Tournaments
                .Where(t => t.Id == id)
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
    /// Get tournament community logo (public, no auth required)
    /// </summary>
    [HttpGet("{id}/logo")]
    public async Task<IActionResult> GetTournamentLogo(int id)
    {
        try
        {
            var tournament = await context.Tournaments
                .Where(t => t.Id == id)
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

    /// <summary>
    /// Get leaderboard rankings for a tournament (public, no auth required)
    /// Optional week parameter defaults to cumulative standings if not specified
    /// </summary>
    [HttpGet("{idOrName}/leaderboard")]
    public async Task<ActionResult<PublicTournamentLeaderboardResponse>> GetLeaderboard(
        string idOrName,
        string? week = null)
    {
        try
        {
            Tournament? tournament;

            // Try to parse as integer first (ID lookup)
            if (int.TryParse(idOrName, out int id))
            {
                tournament = await context.Tournaments
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                // If not a number, search by name
                tournament = await context.Tournaments
                    .FirstOrDefaultAsync(t => t.Name == idOrName);
            }

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get rankings for this tournament and week
            // If week is null, returns cumulative standings (Week == null)
            // If week is specified, returns week-specific standings
            var rankings = await context.TournamentTeamRankings
                .Where(r => r.TournamentId == tournament.Id && r.Week == week)
                .OrderBy(r => r.Rank)
                .Select(r => new PublicTeamRankingResponse
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
                    TotalRounds = r.RoundsWon + r.RoundsTied + r.RoundsLost
                })
                .ToListAsync();

            var response = new PublicTournamentLeaderboardResponse
            {
                TournamentId = tournament.Id,
                TournamentName = tournament.Name,
                Week = week,
                Rankings = rankings
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting leaderboard for tournament {TournamentId}, week {Week}", idOrName, week);
            return StatusCode(500, new { message = "Error retrieving leaderboard" });
        }
    }

    /// <summary>
    /// Get files and comments for a specific match (public, no auth required)
    /// Allows fetching match recordings, map packs, and organizer notes on demand
    /// </summary>
    [HttpGet("{tournamentId}/matches/{matchId}/files-and-comments")]
    public async Task<ActionResult<PublicMatchFilesAndCommentsResponse>> GetMatchFilesAndComments(
        int tournamentId,
        int matchId)
    {
        try
        {
            // Verify tournament exists and is not draft
            var tournament = await context.Tournaments
                .Where(t => t.Id == tournamentId && t.Status != "draft")
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
                .Select(f => new PublicMatchFileResponse(
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
                .Select(c => new PublicMatchCommentResponse(
                    c.Id,
                    c.Content,
                    c.CreatedByUser != null ? c.CreatedByUser.Email : "Unknown",
                    c.CreatedAt,
                    c.UpdatedAt))
                .ToListAsync();

            await Task.WhenAll(filesTask, commentsTask);

            var files = await filesTask;
            var comments = await commentsTask;

            var response = new PublicMatchFilesAndCommentsResponse
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
}

// Response DTOs for public endpoints
// Theme DTOs for public API
public class PublicTournamentThemeResponse
{
    public string? BackgroundColour { get; set; }
    public string? TextColour { get; set; }
    public string? AccentColour { get; set; }
}

// File DTOs for public API
public record PublicTournamentFileResponse(
    int Id,
    string Name,
    string Url,
    string? Category,
    Instant UploadedAt);

public class PublicTournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public string Status { get; set; } = ""; // draft, registration, open, closed
    public string? GameMode { get; set; } // Conquest, CTF, etc.
    public List<PublicTournamentTeamResponse> Teams { get; set; } = [];
    public List<PublicMatchWeekGroup> MatchesByWeek { get; set; } = [];
    public List<PublicTournamentMatchResponse> LatestMatches { get; set; } = []; // 2 most recent completed matches
    public List<PublicTournamentFileResponse> Files { get; set; } = [];
    public bool HasHeroImage { get; set; }
    public bool HasCommunityLogo { get; set; }
    public string? Rules { get; set; }
    public string? RegistrationRules { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public string? YouTubeUrl { get; set; }
    public string? TwitchUrl { get; set; }
    public PublicTournamentThemeResponse? Theme { get; set; }
}

public class PublicTournamentTeamResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Tag { get; set; }
    public Instant CreatedAt { get; set; }
    public string? LeaderPlayerName { get; set; }
    public List<PublicTournamentTeamPlayerResponse> Players { get; set; } = [];
}

public class PublicTournamentTeamPlayerResponse
{
    public string PlayerName { get; set; } = "";
    public bool IsLeader { get; set; }
}

public class PublicTournamentMatchResponse
{
    public int Id { get; set; }
    public Instant ScheduledDate { get; set; }
    public string Team1Name { get; set; } = "";
    public string Team2Name { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? Week { get; set; }
    public Instant CreatedAt { get; set; }
    public List<PublicTournamentMatchMapResponse> Maps { get; set; } = [];
}

public class PublicTournamentMatchMapResponse
{
    public int Id { get; set; }
    public string MapName { get; set; } = "";
    public int MapOrder { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ImagePath { get; set; } // e.g., "golden-gun/map1.png"
    public List<PublicTournamentMatchResultResponse> MatchResults { get; set; } = [];
}

public class PublicTournamentMatchResultResponse
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

public class PublicMatchWeekGroup
{
    public string? Week { get; set; }
    public List<PublicTournamentMatchResponse> Matches { get; set; } = [];
}

public class PublicTournamentRoundResponse
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
    public string? WinningTeamName { get; set; }
    public List<PublicRoundPlayerResponse> Players { get; set; } = [];
}

public class PublicRoundPlayerResponse
{
    public string PlayerName { get; set; } = "";
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";
}

public class PublicTournamentLeaderboardResponse
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = "";
    public string? Week { get; set; }
    public List<PublicTeamRankingResponse> Rankings { get; set; } = [];
}

public class PublicTeamRankingResponse
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = "";

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

    // Calculated field for compatibility
    public int TotalRounds { get; set; }
}

// Match Files and Comments DTOs for public endpoint
public class PublicMatchFilesAndCommentsResponse
{
    public int TournamentId { get; set; }
    public int MatchId { get; set; }
    public List<PublicMatchFileResponse> Files { get; set; } = [];
    public List<PublicMatchCommentResponse> Comments { get; set; } = [];
}

public record PublicMatchFileResponse(
    int Id,
    string Name,
    string Url,
    string? Tags,
    Instant UploadedAt);

public record PublicMatchCommentResponse(
    int Id,
    string Content,
    string CreatedByUserEmail,
    Instant CreatedAt,
    Instant UpdatedAt);
