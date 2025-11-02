using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;
using NodaTime;

namespace junie_des_1942stats.Services.Tournament;

public class TeamRankingCalculator : ITeamRankingCalculator
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<TeamRankingCalculator> _logger;

    public TeamRankingCalculator(PlayerTrackerDbContext dbContext, ILogger<TeamRankingCalculator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<TournamentTeamRanking>> CalculateRankingsAsync(int tournamentId, string? week = null)
    {
        try
        {
            _logger.LogInformation(
                "Starting ranking calculation | TournamentId={TournamentId} Week={Week}",
                tournamentId, week ?? "cumulative");

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

            // Group results by team and aggregate statistics
            var teamIds = matchResults
                .SelectMany(mr => new[] { mr.Team1Id, mr.Team2Id })
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Unique teams identified | TournamentId={TournamentId} Week={Week} TeamCount={TeamCount}",
                tournamentId, week ?? "cumulative", teamIds.Count);

            var teamStats = new Dictionary<int, (int RoundsWon, int RoundsTied, int RoundsLost, int TicketDifferential)>();

            foreach (var teamId in teamIds)
            {
                var stats = CalculateTeamStatistics(matchResults, teamId, tournamentId);
                teamStats[teamId] = stats;

                _logger.LogInformation(
                    "Team statistics calculated | TournamentId={TournamentId} Week={Week} TeamId={TeamId} RoundsWon={Won} RoundsTied={Tied} RoundsLost={Lost} TicketDiff={Differential}",
                    tournamentId, week ?? "cumulative", teamId, stats.RoundsWon, stats.RoundsTied, stats.RoundsLost, stats.TicketDifferential);
            }

            // Sort by ranking criteria (hierarchical)
            var rankedTeams = teamStats
                .OrderByDescending(kvp => kvp.Value.RoundsWon)        // Primary: Rounds won
                .ThenByDescending(kvp => kvp.Value.RoundsTied)        // Tier 1a: Rounds tied (prefer tied over lost)
                .ThenByDescending(kvp => kvp.Value.TicketDifferential) // Tier 2: Ticket differential
                .ToList();

            _logger.LogInformation(
                "Teams sorted by ranking criteria | TournamentId={TournamentId} Week={Week} Criteria=(RoundsWon > RoundsTied > TicketDifferential)",
                tournamentId, week ?? "cumulative");

            // Create ranking records with assigned positions
            var rankings = new List<TournamentTeamRanking>();
            for (int i = 0; i < rankedTeams.Count; i++)
            {
                var teamId = rankedTeams[i].Key;
                var stats = rankedTeams[i].Value;
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
                    Rank = rank,
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                rankings.Add(ranking);

                _logger.LogInformation(
                    "Team ranking assigned | TournamentId={TournamentId} Week={Week} TeamId={TeamId} Rank={Rank} W-T-L={Won}-{Tied}-{Lost} TicketDiff={Differential}",
                    tournamentId, week ?? "cumulative", teamId, rank, stats.RoundsWon, stats.RoundsTied, stats.RoundsLost, stats.TicketDifferential);
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

    public async Task<int> RecalculateAllRankingsAsync(int tournamentId)
    {
        try
        {
            _logger.LogInformation(
                "Starting full ranking recalculation | TournamentId={TournamentId}",
                tournamentId);

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

                    var rankings = await CalculateRankingsAsync(tournamentId, week);

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
    private (int RoundsWon, int RoundsTied, int RoundsLost, int TicketDifferential) CalculateTeamStatistics(
        List<TournamentMatchResult> matchResults,
        int teamId,
        int tournamentId)
    {
        int roundsWon = 0;
        int roundsTied = 0;
        int roundsLost = 0;
        int ticketDifferential = 0;

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

            // Determine result: win, tie, or loss
            if (result.WinningTeamId == teamId)
            {
                roundsWon++;
            }
            else if (result.WinningTeamId == 0 || (isTeam1 && result.Team1Tickets == result.Team2Tickets) ||
                     (isTeam2 && result.Team2Tickets == result.Team1Tickets))
            {
                // Tie condition: equal tickets
                roundsTied++;
            }
            else
            {
                roundsLost++;
            }
        }

        return (roundsWon, roundsTied, roundsLost, ticketDifferential);
    }
}
