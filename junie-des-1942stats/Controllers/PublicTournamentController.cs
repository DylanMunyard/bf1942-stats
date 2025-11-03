using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.PlayerTracking;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/tournaments")]
public class PublicTournamentController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly ILogger<PublicTournamentController> _logger;

    public PublicTournamentController(
        PlayerTrackerDbContext context,
        ILogger<PublicTournamentController> logger)
    {
        _context = context;
        _logger = logger;
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
                tournament = await _context.Tournaments
                    .Include(t => t.OrganizerPlayer)
                    .Include(t => t.Server)
                    .Include(t => t.Theme)
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                // If not a number, search by name
                tournament = await _context.Tournaments
                    .Include(t => t.OrganizerPlayer)
                    .Include(t => t.Server)
                    .Include(t => t.Theme)
                    .FirstOrDefaultAsync(t => t.Name == idOrName);
            }

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Rest of the existing logic stays the same...
            var tournamentId = tournament.Id;

            // Load teams for this tournament
            var teams = await _context.TournamentTeams
                .Where(tt => tt.TournamentId == tournamentId)
                .Select(tt => new { tt.Id, tt.Name, tt.CreatedAt })
                .ToListAsync();

            // [rest of the existing implementation continues unchanged...]
            var teamIds = teams.Select(t => t.Id).ToList();

            // Batch load all team players
            var teamPlayers = await _context.TournamentTeamPlayers
                .Where(ttp => teamIds.Contains(ttp.TournamentTeamId))
                .Select(ttp => new { ttp.TournamentTeamId, ttp.PlayerName })
                .ToListAsync();

            var teamPlayersLookup = teamPlayers
                .GroupBy(tp => tp.TournamentTeamId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.PlayerName).ToList());

            // Build team responses
            var teamResponses = teams.Select(t => new PublicTournamentTeamResponse
            {
                Id = t.Id,
                Name = t.Name,
                CreatedAt = t.CreatedAt,
                Players = teamPlayersLookup.GetValueOrDefault(t.Id, new List<string>())
                    .Select(pn => new PublicTournamentTeamPlayerResponse { PlayerName = pn })
                    .ToList()
            }).ToList();

            // Load matches for this tournament
            var matches = await _context.TournamentMatches
                .Where(tm => tm.TournamentId == tournamentId)
                .Select(tm => new
                {
                    tm.Id,
                    tm.ScheduledDate,
                    tm.Team1Id,
                    tm.Team2Id,
                    Team1Name = tm.Team1.Name,
                    Team2Name = tm.Team2.Name,
                    tm.ServerGuid,
                    tm.ServerName,
                    tm.Week,
                    tm.CreatedAt
                })
                .ToListAsync();

            var matchIds = matches.Select(m => m.Id).ToList();

            // Batch load all match maps with their match results
            var matchMaps = await _context.TournamentMatchMaps
                .Where(tmm => matchIds.Contains(tmm.MatchId))
                .Select(tmm => new
                {
                    tmm.Id,
                    tmm.MatchId,
                    tmm.MapName,
                    tmm.MapOrder,
                    tmm.TeamId,
                    TeamName = tmm.Team != null ? tmm.Team.Name : null,
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
                .ToListAsync();

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
                Teams = teamResponses,
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
            _logger.LogError(ex, "Error getting public tournament {TournamentId}", idOrName);
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
            var tournament = await _context.Tournaments
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
            _logger.LogError(ex, "Error getting tournament image {TournamentId}", id);
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
            var tournament = await _context.Tournaments
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
            _logger.LogError(ex, "Error getting tournament logo {TournamentId}", id);
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
                tournament = await _context.Tournaments
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                // If not a number, search by name
                tournament = await _context.Tournaments
                    .FirstOrDefaultAsync(t => t.Name == idOrName);
            }

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get rankings for this tournament and week
            // If week is null, returns cumulative standings (Week == null)
            // If week is specified, returns week-specific standings
            var rankings = await _context.TournamentTeamRankings
                .Where(r => r.TournamentId == tournament.Id && r.Week == week)
                .OrderBy(r => r.Rank)
                .Select(r => new PublicTeamRankingResponse
                {
                    Rank = r.Rank,
                    TeamId = r.TeamId,
                    TeamName = r.Team != null ? r.Team.Name : $"Team {r.TeamId}",
                    RoundsWon = r.RoundsWon,
                    RoundsTied = r.RoundsTied,
                    RoundsLost = r.RoundsLost,
                    TicketDifferential = r.TicketDifferential,
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
            _logger.LogError(ex, "Error getting leaderboard for tournament {TournamentId}, week {Week}", idOrName, week);
            return StatusCode(500, new { message = "Error retrieving leaderboard" });
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

public class PublicTournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string Game { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }
    public List<PublicTournamentTeamResponse> Teams { get; set; } = [];
    public List<PublicMatchWeekGroup> MatchesByWeek { get; set; } = [];
    public bool HasHeroImage { get; set; }
    public bool HasCommunityLogo { get; set; }
    public string? Rules { get; set; }
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public PublicTournamentThemeResponse? Theme { get; set; }
}

public class PublicTournamentTeamResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Instant CreatedAt { get; set; }
    public List<PublicTournamentTeamPlayerResponse> Players { get; set; } = [];
}

public class PublicTournamentTeamPlayerResponse
{
    public string PlayerName { get; set; } = "";
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
    public int RoundsWon { get; set; }
    public int RoundsTied { get; set; }
    public int RoundsLost { get; set; }
    public int TicketDifferential { get; set; }
    public int TotalRounds { get; set; }
}
