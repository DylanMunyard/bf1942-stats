using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Services.Tournament;

public class TeamMappingService : ITeamMappingService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<TeamMappingService> _logger;

    public TeamMappingService(PlayerTrackerDbContext dbContext, ILogger<TeamMappingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(int Team1Id, int Team2Id, string? WarningMessage)> DetectTeamMappingAsync(string roundId, int tournamentId)
    {
        try
        {
            _logger.LogInformation(
                "Starting team mapping detection | RoundId={RoundId} TournamentId={TournamentId}",
                roundId, tournamentId);

            // Get the round data with player sessions
            var round = await _dbContext.Rounds
                .Include(r => r.Sessions)
                .FirstOrDefaultAsync(r => r.RoundId == roundId);

            if (round == null)
            {
                var warning = $"Round {roundId} not found";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", warning);
                return (0, 0, warning);
            }

            _logger.LogInformation(
                "Round loaded | RoundId={RoundId} Team1Label={Team1Label} Team2Label={Team2Label} SessionCount={SessionCount}",
                roundId, round.Team1Label ?? "null", round.Team2Label ?? "null", round.Sessions.Count);

            if (!round.Sessions.Any())
            {
                var warning = $"Round {roundId} has no player sessions - cannot detect teams";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", warning);
                return (0, 0, warning);
            }

            // Get tournament teams with their rosters
            var tournamentTeams = await _dbContext.TournamentTeams
                .Where(t => t.TournamentId == tournamentId)
                .Include(t => t.TeamPlayers)
                .ToListAsync();

            _logger.LogInformation(
                "Tournament teams loaded | TournamentId={TournamentId} TeamCount={TeamCount}",
                tournamentId, tournamentTeams.Count);

            if (!tournamentTeams.Any())
            {
                var warning = $"Tournament {tournamentId} has no teams configured";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", warning);
                return (0, 0, warning);
            }

            // Group player sessions by team label to get Team1 and Team2 rosters
            var team1Sessions = round.Sessions.Where(s => s.CurrentTeamLabel == round.Team1Label).ToList();
            var team2Sessions = round.Sessions.Where(s => s.CurrentTeamLabel == round.Team2Label).ToList();

            _logger.LogInformation(
                "Round teams parsed | Team1Players={Team1Count} Team2Players={Team2Count}",
                team1Sessions.Count, team2Sessions.Count);

            if (!team1Sessions.Any() || !team2Sessions.Any())
            {
                var warning = "Could not identify both teams in round data";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", warning);
                return (0, 0, warning);
            }

            // Extract player names for each team
            var team1Players = new HashSet<string>(team1Sessions.Select(s => s.PlayerName), StringComparer.OrdinalIgnoreCase);
            var team2Players = new HashSet<string>(team2Sessions.Select(s => s.PlayerName), StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug(
                "Round player rosters | Team1={Team1Players} Team2={Team2Players}",
                string.Join(", ", team1Players.Take(5)), string.Join(", ", team2Players.Take(5)));

            // For each tournament team, check how many roster players appear in team1 vs team2
            var mappingScores = new Dictionary<int, (int Team1Matches, int Team2Matches)>();

            foreach (var team in tournamentTeams)
            {
                int team1MatchCount = 0;
                int team2MatchCount = 0;
                var matchedPlayersTeam1 = new List<string>();
                var matchedPlayersTeam2 = new List<string>();

                foreach (var rosterPlayer in team.TeamPlayers)
                {
                    if (team1Players.Contains(rosterPlayer.PlayerName))
                    {
                        team1MatchCount++;
                        matchedPlayersTeam1.Add(rosterPlayer.PlayerName);
                    }

                    if (team2Players.Contains(rosterPlayer.PlayerName))
                    {
                        team2MatchCount++;
                        matchedPlayersTeam2.Add(rosterPlayer.PlayerName);
                    }
                }

                // Only consider if at least one player matched
                if (team1MatchCount > 0 || team2MatchCount > 0)
                {
                    mappingScores[team.Id] = (team1MatchCount, team2MatchCount);
                    _logger.LogInformation(
                        "Tournament team evaluated | TeamId={TeamId} TeamName={TeamName} Team1Matches={Team1Matches} Team2Matches={Team2Matches} MatchedPlayers1={Players1} MatchedPlayers2={Players2}",
                        team.Id, team.Name, team1MatchCount, team2MatchCount,
                        string.Join(", ", matchedPlayersTeam1), string.Join(", ", matchedPlayersTeam2));
                }
                else
                {
                    _logger.LogDebug(
                        "Tournament team skipped (no player matches) | TeamId={TeamId} TeamName={TeamName}",
                        team.Id, team.Name);
                }
            }

            // Determine mapping: each tournament team should match primarily one round team
            var viableTeams = mappingScores
                .Where(kvp => kvp.Value.Team1Matches > 0 || kvp.Value.Team2Matches > 0)
                .ToList();

            _logger.LogInformation(
                "Viable teams after filtering | ViableTeamCount={ViableTeamCount} MinRequired=2",
                viableTeams.Count);

            if (viableTeams.Count < 2)
            {
                var msg = viableTeams.Count == 0
                    ? "No tournament teams matched players in round"
                    : "Only one tournament team matched players - need at least two teams";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", msg);
                return (0, 0, msg);
            }

            // Sort by team1 matches descending to find best team for team1
            var bestForTeam1 = viableTeams
                .OrderByDescending(kvp => kvp.Value.Team1Matches)
                .ThenByDescending(kvp => kvp.Value.Team1Matches - kvp.Value.Team2Matches)
                .First();

            _logger.LogInformation(
                "Best match for RoundTeam1 selected | TournamentTeamId={TeamId} Team1Matches={Team1Matches} Team2Matches={Team2Matches} Confidence={Confidence:P}",
                bestForTeam1.Key, bestForTeam1.Value.Team1Matches, bestForTeam1.Value.Team2Matches,
                (double)bestForTeam1.Value.Team1Matches / (bestForTeam1.Value.Team1Matches + bestForTeam1.Value.Team2Matches));

            // For team2, pick the best match that's not already assigned to team1
            var bestForTeam2 = viableTeams
                .Where(kvp => kvp.Key != bestForTeam1.Key)
                .OrderByDescending(kvp => kvp.Value.Team2Matches)
                .ThenByDescending(kvp => kvp.Value.Team2Matches - kvp.Value.Team1Matches)
                .FirstOrDefault();

            if (bestForTeam2.Key == 0)
            {
                var warning = "Could not find a second tournament team for team2 mapping";
                _logger.LogWarning("Team mapping detection failed | Reason={Reason}", warning);
                return (0, 0, warning);
            }

            _logger.LogInformation(
                "Best match for RoundTeam2 selected | TournamentTeamId={TeamId} Team1Matches={Team1Matches} Team2Matches={Team2Matches} Confidence={Confidence:P}",
                bestForTeam2.Key, bestForTeam2.Value.Team1Matches, bestForTeam2.Value.Team2Matches,
                (double)bestForTeam2.Value.Team2Matches / (bestForTeam2.Value.Team1Matches + bestForTeam2.Value.Team2Matches));

            var team1Id = bestForTeam1.Key;
            var team2Id = bestForTeam2.Key;

            _logger.LogInformation(
                "Team mapping detection SUCCESS | RoundId={RoundId} TournamentTeam1Id={Team1Id} -> RoundTeam1 TournamentTeam2Id={Team2Id} -> RoundTeam2",
                roundId, team1Id, team2Id);

            return (team1Id, team2Id, null); // Success!
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Team mapping detection FAILED with exception | RoundId={RoundId} TournamentId={TournamentId}",
                roundId, tournamentId);
            return (0, 0, $"Error during team detection: {ex.Message}");
        }
    }
}
