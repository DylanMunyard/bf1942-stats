using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using api.PlayerTracking;
using NodaTime;

namespace api.Gamification.Services;

public class TeamRankingCalculator : ITeamRankingCalculator
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<TeamRankingCalculator> _logger;

    public TeamRankingCalculator(PlayerTrackerDbContext dbContext, ILogger<TeamRankingCalculator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<TournamentTeamRanking>> CalculateRankingsAsync(int tournamentId, string? week = null, string? gameMode = null)
    {
        try
        {
            var isCTF = string.Equals(gameMode, "CTF", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Starting ranking calculation | TournamentId={TournamentId} Week={Week} GameMode={GameMode} IsCTF={IsCTF}",
                tournamentId, week ?? "cumulative", gameMode ?? "default", isCTF);

            // Get all match results for this tournament and optional week filter
            var query = _dbContext.TournamentMatchResults
                .Where(mr => mr.TournamentId == tournamentId);

            if (week != null)
                query = query.Where(mr => mr.Week == week);

            var matchResults = await query.ToListAsync();

            _logger.LogInformation(
                "Match results loaded | TournamentId={TournamentId} Week={Week} ResultCount={ResultCount}",
                tournamentId, week ?? "cumulative", matchResults.Count);

            if (!matchResults.Any())
            {
                _logger.LogWarning(
                    "No match results found for ranking calculation | TournamentId={TournamentId} Week={Week}",
                    tournamentId, week ?? "cumulative");
                return [];
            }

            // Group results by team and aggregate statistics (filter out null team IDs)
            var teamIds = matchResults
                .SelectMany(mr => new[] { mr.Team1Id, mr.Team2Id })
                .Where(teamId => teamId.HasValue)
                .Select(teamId => teamId!.Value)
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Unique teams identified | TournamentId={TournamentId} Week={Week} TeamCount={TeamCount}",
                tournamentId, week ?? "cumulative", teamIds.Count);

            var teamStats = new Dictionary<int, (int RoundsWon, int RoundsTied, int RoundsLost, int TicketDifferential, int MatchesPlayed, int Victories, int Ties, int Losses, int TicketsFor, int TicketsAgainst, int Points)>();

            foreach (var teamId in teamIds)
            {
                var stats = CalculateTeamStatistics(matchResults, teamId, tournamentId, isCTF);
                teamStats[teamId] = stats;

                _logger.LogInformation(
                    "Team statistics calculated | TournamentId={TournamentId} Week={Week} TeamId={TeamId} Points={Points} RoundsWon={RoundsWon} RoundsTied={RoundsTied} RoundsLost={RoundsLost} Matches={Matches} Victories={Victories} Ties={Ties} Losses={Losses} TicketsFor={TicketsFor} TicketsAgainst={TicketsAgainst} TicketDiff={TicketDiff}",
                    tournamentId, week ?? "cumulative", teamId, stats.Points, stats.RoundsWon, stats.RoundsTied, stats.RoundsLost, stats.MatchesPlayed, stats.Victories, stats.Ties, stats.Losses, stats.TicketsFor, stats.TicketsAgainst, stats.TicketDifferential);
            }

            // Sort by ranking criteria (hierarchical per spec)
            // CTF: Points (match-based: 3*wins + 1*draws) > Ticket differential
            // Default: Points (= RoundsWon) > RoundsTied > -RoundsLost > Ticket differential
            IOrderedEnumerable<KeyValuePair<int, (int RoundsWon, int RoundsTied, int RoundsLost, int TicketDifferential, int MatchesPlayed, int Victories, int Ties, int Losses, int TicketsFor, int TicketsAgainst, int Points)>> rankedTeams;

            if (isCTF)
            {
                // CTF scoring: Points (match-based) > Flag differential
                rankedTeams = teamStats
                    .OrderByDescending(kvp => kvp.Value.Points)             // PRIMARY: Points (= 3*wins + 1*draws)
                    .ThenByDescending(kvp => kvp.Value.TicketDifferential); // TIER 2: Flag differential

                _logger.LogInformation(
                    "Teams sorted by CTF ranking criteria | TournamentId={TournamentId} Week={Week} Criteria=(Points[match-based] > FlagDifferential)",
                    tournamentId, week ?? "cumulative");
            }
            else
            {
                // Default (Conquest) scoring: Points (round-based) > RoundsTied > -RoundsLost > Ticket differential
                rankedTeams = teamStats
                    .OrderByDescending(kvp => kvp.Value.Points)             // PRIMARY: Points (= RoundsWon)
                    .ThenByDescending(kvp => kvp.Value.RoundsTied)          // TIER 1a: Rounds tied (prefer tied)
                    .ThenByDescending(kvp => -kvp.Value.RoundsLost)         // TIER 1b: Rounds lost (ascending = prefer fewer losses)
                    .ThenByDescending(kvp => kvp.Value.TicketDifferential); // TIER 2: Ticket differential

                _logger.LogInformation(
                    "Teams sorted by ranking criteria | TournamentId={TournamentId} Week={Week} Criteria=(Points > RoundsTied > -RoundsLost > TicketDifferential)",
                    tournamentId, week ?? "cumulative");
            }

            var rankedTeamsList = rankedTeams.ToList();

            // Create ranking records with assigned positions
            var rankings = new List<TournamentTeamRanking>();
            for (int i = 0; i < rankedTeamsList.Count; i++)
            {
                var teamId = rankedTeamsList[i].Key;
                var stats = rankedTeamsList[i].Value;
                var rank = i + 1;

                var ranking = new TournamentTeamRanking
                {
                    TournamentId = tournamentId,
                    TeamId = teamId,
                    Week = week,
                    RoundsWon = stats.RoundsWon,
                    RoundsTied = stats.RoundsTied,
                    RoundsLost = stats.RoundsLost,
                    TicketDifferential = stats.TicketDifferential,
                    MatchesPlayed = stats.MatchesPlayed,
                    Victories = stats.Victories,
                    Ties = stats.Ties,
                    Losses = stats.Losses,
                    TicketsFor = stats.TicketsFor,
                    TicketsAgainst = stats.TicketsAgainst,
                    Points = stats.Points,
                    Rank = rank,
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                rankings.Add(ranking);

                _logger.LogInformation(
                    "Team ranking assigned | TournamentId={TournamentId} Week={Week} TeamId={TeamId} Rank={Rank} Points={Points} Matches={Matches} (V-T-L={Victories}-{Ties}-{Losses}) Rounds=(W-T-L={RoundsWon}-{RoundsTied}-{RoundsLost}) Tickets=({TicketsFor}-{TicketsAgainst}={TicketDiff})",
                    tournamentId, week ?? "cumulative", teamId, rank, stats.Points, stats.MatchesPlayed, stats.Victories, stats.Ties, stats.Losses, stats.RoundsWon, stats.RoundsTied, stats.RoundsLost, stats.TicketsFor, stats.TicketsAgainst, stats.TicketDifferential);
            }

            _logger.LogInformation(
                "Ranking calculation completed successfully | TournamentId={TournamentId} Week={Week} RankingCount={RankingCount}",
                tournamentId, week ?? "cumulative", rankings.Count);

            return rankings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ranking calculation FAILED | TournamentId={TournamentId} Week={Week}",
                tournamentId, week ?? "cumulative");
            throw;
        }
    }

    public async Task<int> RecalculateAllRankingsAsync(int tournamentId, string? gameMode = null)
    {
        try
        {
            _logger.LogInformation(
                "Starting full ranking recalculation | TournamentId={TournamentId} GameMode={GameMode}",
                tournamentId, gameMode ?? "default");

            // Get all distinct weeks for this tournament, plus null for cumulative
            var weeksInTournament = await _dbContext.TournamentMatchResults
                .Where(mr => mr.TournamentId == tournamentId)
                .Select(mr => mr.Week)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation(
                "Distinct weeks identified for recalculation | TournamentId={TournamentId} WeekCount={WeekCount} Weeks={Weeks}",
                tournamentId, weeksInTournament.Count, string.Join(", ", weeksInTournament.Select(w => w ?? "cumulative")));

            // Always include null for cumulative standings
            if (!weeksInTournament.Contains(null))
            {
                weeksInTournament.Add(null);
                _logger.LogInformation("Added cumulative week for ranking recalculation | TournamentId={TournamentId}", tournamentId);
            }

            int totalUpdated = 0;

            // Recalculate for each week + cumulative
            foreach (var week in weeksInTournament)
            {
                try
                {
                    _logger.LogInformation(
                        "Recalculating rankings for week | TournamentId={TournamentId} Week={Week}",
                        tournamentId, week ?? "cumulative");

                    var rankings = await CalculateRankingsAsync(tournamentId, week, gameMode);

                    // Delete old rankings for this tournament/week combo
                    var oldRankings = await _dbContext.TournamentTeamRankings
                        .Where(r => r.TournamentId == tournamentId && r.Week == week)
                        .ToListAsync();

                    _logger.LogInformation(
                        "Deleting old rankings | TournamentId={TournamentId} Week={Week} OldRankingCount={OldCount}",
                        tournamentId, week ?? "cumulative", oldRankings.Count);

                    _dbContext.TournamentTeamRankings.RemoveRange(oldRankings);

                    // Insert new rankings
                    await _dbContext.TournamentTeamRankings.AddRangeAsync(rankings);
                    await _dbContext.SaveChangesAsync();

                    totalUpdated += rankings.Count;

                    _logger.LogInformation(
                        "Rankings persisted to database | TournamentId={TournamentId} Week={Week} NewRankingCount={NewCount} TotalUpdated={TotalUpdated}",
                        tournamentId, week ?? "cumulative", rankings.Count, totalUpdated);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error recalculating rankings for week | TournamentId={TournamentId} Week={Week}",
                        tournamentId, week ?? "cumulative");
                    throw;
                }
            }

            _logger.LogInformation(
                "Full ranking recalculation completed successfully | TournamentId={TournamentId} TotalRankingsUpdated={TotalUpdated}",
                tournamentId, totalUpdated);

            return totalUpdated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Full ranking recalculation FAILED | TournamentId={TournamentId}",
                tournamentId);
            throw;
        }
    }

    /// <summary>
    /// Calculate aggregate statistics for a specific team from match results.
    /// </summary>
    private (int RoundsWon, int RoundsTied, int RoundsLost, int TicketDifferential, int MatchesPlayed, int Victories, int Ties, int Losses, int TicketsFor, int TicketsAgainst, int Points) CalculateTeamStatistics(
        List<TournamentMatchResult> matchResults,
        int teamId,
        int tournamentId,
        bool isCTF = false)
    {
        _logger.LogDebug("Starting statistics calculation for TeamId={TeamId} IsCTF={IsCTF}", teamId, isCTF);

        int roundsWon = 0;
        int roundsTied = 0;
        int roundsLost = 0;
        int ticketDifferential = 0;
        int ticketsFor = 0;
        int ticketsAgainst = 0;

        // For match-level statistics, group rounds by match
        var roundsByMatch = new Dictionary<int, List<TournamentMatchResult>>();
        var roundDetails = new List<string>(); // For logging

        foreach (var result in matchResults)
        {
            // Determine if this result involves the team
            bool isTeam1 = result.Team1Id == teamId;
            bool isTeam2 = result.Team2Id == teamId;

            if (!isTeam1 && !isTeam2)
                continue;

            // Calculate ticket differential for this round
            int teamTickets = isTeam1 ? result.Team1Tickets : result.Team2Tickets;
            int opponentTickets = isTeam1 ? result.Team2Tickets : result.Team1Tickets;
            int diff = teamTickets - opponentTickets;

            ticketDifferential += diff;
            ticketsFor += teamTickets;
            ticketsAgainst += opponentTickets;

            // Determine result: win, tie, or loss
            string roundOutcome;
            if (result.WinningTeamId == teamId)
            {
                roundsWon++;
                roundOutcome = "WIN";
            }
            else if (result.WinningTeamId == 0 || (isTeam1 && result.Team1Tickets == result.Team2Tickets) ||
                     (isTeam2 && result.Team2Tickets == result.Team1Tickets))
            {
                // Tie condition: equal tickets
                roundsTied++;
                roundOutcome = "TIE";
            }
            else
            {
                roundsLost++;
                roundOutcome = "LOSS";
            }

            roundDetails.Add($"MatchId={result.MatchId} MapId={result.MapId} {roundOutcome} Tickets={teamTickets}v{opponentTickets}");

            // Group by match for match-level calculations
            if (!roundsByMatch.ContainsKey(result.MatchId))
            {
                roundsByMatch[result.MatchId] = [];
            }
            roundsByMatch[result.MatchId].Add(result);
        }

        // Log all round details
        if (roundDetails.Any())
        {
            _logger.LogDebug(
                "Round-level results for TeamId={TeamId} | Total Rounds Processed={Count} | Details: {RoundDetails}",
                teamId, roundDetails.Count, string.Join(" | ", roundDetails));
        }
        else
        {
            _logger.LogDebug("No rounds found for TeamId={TeamId}", teamId);
        }

        // Calculate match-level statistics
        int matchesPlayed = 0;
        int victories = 0;
        int ties = 0;
        int losses = 0;
        var matchDetails = new List<string>(); // For logging

        foreach (var matchId in roundsByMatch.Keys.OrderBy(m => m))
        {
            var roundsInMatch = roundsByMatch[matchId];

            // Sum tickets for team and opponent across all rounds in this match
            int teamTotalTickets = 0;
            int opponentTotalTickets = 0;
            var roundsInMatchDetails = new List<string>();

            foreach (var result in roundsInMatch)
            {
                bool isTeam1 = result.Team1Id == teamId;
                int teamRoundTickets = isTeam1 ? result.Team1Tickets : result.Team2Tickets;
                int opponentRoundTickets = isTeam1 ? result.Team2Tickets : result.Team1Tickets;

                teamTotalTickets += teamRoundTickets;
                opponentTotalTickets += opponentRoundTickets;

                roundsInMatchDetails.Add($"Round{roundsInMatch.IndexOf(result) + 1}(Map={result.MapId}):{teamRoundTickets}v{opponentRoundTickets}");
            }

            matchesPlayed++;

            // Determine match outcome based on total tickets
            string matchOutcome;
            if (teamTotalTickets > opponentTotalTickets)
            {
                victories++;
                matchOutcome = "WIN";
            }
            else if (teamTotalTickets == opponentTotalTickets)
            {
                ties++;
                matchOutcome = "TIE";
            }
            else
            {
                losses++;
                matchOutcome = "LOSS";
            }

            matchDetails.Add($"MatchId={matchId} {matchOutcome} Rounds=[{string.Join(",", roundsInMatchDetails)}] Total={teamTotalTickets}v{opponentTotalTickets}");

            _logger.LogDebug(
                "Match aggregation for TeamId={TeamId} MatchId={MatchId} | Outcome={Outcome} | Total Tickets={TeamTotal}v{OpponentTotal} | Rounds={RoundsInMatch}",
                teamId, matchId, matchOutcome, teamTotalTickets, opponentTotalTickets, string.Join(" ", roundsInMatchDetails));
        }

        // Log match-level summary
        if (matchDetails.Any())
        {
            _logger.LogDebug(
                "Match-level results for TeamId={TeamId} | Total Matches={Count} | Details: {MatchDetails}",
                teamId, matchDetails.Count, string.Join(" | ", matchDetails));
        }

        // Points calculation differs by game mode
        // CTF: 3 points for match win, 1 point for match draw, 0 for loss
        // Default (Conquest): 1 point per round won
        int points = isCTF
            ? (victories * 3) + (ties * 1)
            : roundsWon;

        // Summary log
        _logger.LogDebug(
            "Statistics summary for TeamId={TeamId} | IsCTF={IsCTF} Points={Points} RoundsWon={RoundsWon} RoundsTied={RoundsTied} RoundsLost={RoundsLost} | MatchesPlayed={MatchesPlayed} Victories={Victories} Ties={Ties} Losses={Losses} | TicketsFor={TicketsFor} TicketsAgainst={TicketsAgainst} Differential={TicketDifferential}",
            teamId, isCTF, points, roundsWon, roundsTied, roundsLost, matchesPlayed, victories, ties, losses, ticketsFor, ticketsAgainst, ticketDifferential);

        return (roundsWon, roundsTied, roundsLost, ticketDifferential, matchesPlayed, victories, ties, losses, ticketsFor, ticketsAgainst, points);
    }
}
