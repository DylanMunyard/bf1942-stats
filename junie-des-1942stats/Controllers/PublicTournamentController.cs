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
                    .FirstOrDefaultAsync(t => t.Id == id);
            }
            else
            {
                // If not a number, search by name
                tournament = await _context.Tournaments
                    .Include(t => t.OrganizerPlayer)
                    .Include(t => t.Server)
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

            // Batch load all match maps
            var matchMaps = await _context.TournamentMatchMaps
                .Where(tmm => matchIds.Contains(tmm.MatchId))
                .Select(tmm => new
                {
                    tmm.Id,
                    tmm.MatchId,
                    tmm.MapName,
                    tmm.MapOrder,
                    tmm.RoundId,
                    tmm.TeamId,
                    TeamName = tmm.Team != null ? tmm.Team.Name : null
                })
                .ToListAsync();

            var matchMapsLookup = matchMaps
                .GroupBy(mm => mm.MatchId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.MapOrder).ToList());

            // Collect all round IDs
            var roundIds = matchMaps
                .Where(mm => !string.IsNullOrEmpty(mm.RoundId))
                .Select(mm => mm.RoundId!)
                .Distinct()
                .ToList();

            // Batch load all rounds
            var rounds = await _context.Rounds
                .Where(r => roundIds.Contains(r.RoundId))
                .Select(r => new
                {
                    r.RoundId,
                    r.ServerGuid,
                    r.ServerName,
                    r.MapName,
                    r.StartTime,
                    r.EndTime,
                    r.Tickets1,
                    r.Tickets2,
                    r.Team1Label,
                    r.Team2Label
                })
                .ToListAsync();

            var roundsLookup = rounds.ToDictionary(r => r.RoundId);

            // Batch load all player sessions (scores) for these rounds
            var playerSessions = await _context.PlayerSessions
                .Where(ps => ps.RoundId != null && roundIds.Contains(ps.RoundId))
                .Select(ps => new
                {
                    ps.RoundId,
                    ps.PlayerName,
                    ps.TotalScore,
                    ps.TotalKills,
                    ps.TotalDeaths,
                    ps.CurrentTeam,
                    ps.CurrentTeamLabel
                })
                .ToListAsync();

            var playerSessionsLookup = playerSessions
                .GroupBy(ps => ps.RoundId)
                .ToDictionary(g => g.Key!, g => g.OrderByDescending(ps => ps.TotalScore).ToList());

            // Determine winning teams for each round based on tickets
            var matchRoundWinners = new Dictionary<string, string>();

            foreach (var roundId in roundIds)
            {
                if (roundsLookup.TryGetValue(roundId, out var round))
                {
                    var tickets1 = round.Tickets1 ?? 0;
                    var tickets2 = round.Tickets2 ?? 0;

                    if (tickets1 > tickets2 && round.Team1Label != null)
                    {
                        matchRoundWinners[roundId] = round.Team1Label;
                    }
                    else if (tickets2 > tickets1 && round.Team2Label != null)
                    {
                        matchRoundWinners[roundId] = round.Team2Label;
                    }
                    // If tickets are equal, no winner
                }
            }

            // Build match responses
            var matchResponses = new List<PublicTournamentMatchResponse>();

            foreach (var match in matches.OrderBy(m => m.ScheduledDate))
            {
                var matchMapsForThisMatch = new List<PublicTournamentMatchMapResponse>();

                if (matchMapsLookup.TryGetValue(match.Id, out var mapsForMatch))
                {
                    foreach (var map in mapsForMatch)
                    {
                        PublicTournamentRoundResponse? roundResponse = null;

                        if (!string.IsNullOrEmpty(map.RoundId) && roundsLookup.TryGetValue(map.RoundId, out var round))
                        {
                            string? winningTeamName = null;
                            if (matchRoundWinners.TryGetValue(map.RoundId, out var winner))
                            {
                                winningTeamName = winner;
                            }

                            // Get all players for this round
                            var roundPlayers = new List<PublicRoundPlayerResponse>();
                            if (playerSessionsLookup.TryGetValue(map.RoundId, out var sessions))
                            {
                                roundPlayers = sessions.Select(s => new PublicRoundPlayerResponse
                                {
                                    PlayerName = s.PlayerName,
                                    TotalScore = s.TotalScore,
                                    TotalKills = s.TotalKills,
                                    TotalDeaths = s.TotalDeaths,
                                    Team = s.CurrentTeam,
                                    TeamLabel = s.CurrentTeamLabel
                                }).ToList();
                            }

                            roundResponse = new PublicTournamentRoundResponse
                            {
                                RoundId = round.RoundId,
                                ServerGuid = round.ServerGuid,
                                ServerName = round.ServerName,
                                MapName = round.MapName,
                                StartTime = round.StartTime,
                                EndTime = round.EndTime,
                                Tickets1 = round.Tickets1,
                                Tickets2 = round.Tickets2,
                                Team1Label = round.Team1Label,
                                Team2Label = round.Team2Label,
                                WinningTeamName = winningTeamName,
                                Players = roundPlayers
                            };
                        }

                        matchMapsForThisMatch.Add(new PublicTournamentMatchMapResponse
                        {
                            Id = map.Id,
                            MapName = map.MapName,
                            MapOrder = map.MapOrder,
                            RoundId = map.RoundId,
                            TeamId = map.TeamId,
                            TeamName = map.TeamName,
                            Round = roundResponse
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
                PrimaryColour = tournament.PrimaryColour,
                SecondaryColour = tournament.SecondaryColour
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
}

// Response DTOs for public endpoints
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
    public string? PrimaryColour { get; set; }
    public string? SecondaryColour { get; set; }
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
    public string? RoundId { get; set; }
    public PublicTournamentRoundResponse? Round { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
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
