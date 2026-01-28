using System.Text.Json;
using api.Gamification.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public record TeamVictoryMetadata(
    int WinningTeam,
    string? WinningTeamLabel,
    int WinningTeamTickets,
    int LosingTeam,
    string? LosingTeamLabel,
    int LosingTeamTickets,
    string ServerName,
    int Score,
    int Kills,
    int Deaths,
    int? TotalPlayers,
    double ParticipationWeight,
    double TeamContribution,
    int PlayerObservations,
    int MaxPossibleObservations,
    int TeamObservations,
    bool WasTeamSwitched
);

public class WinningPlayerData
{
    public string RoundId { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public DateTime LastSeenTime { get; set; }
    public int Team { get; set; }
    public string? TeamLabel { get; set; }
}

public class PlayerObservationAnalysis
{
    public string RoundId { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public DateTime LastSeenTime { get; set; }
    public int FinalTeam { get; set; }
    public string? FinalTeamLabel { get; set; }
    public int TotalObservations { get; set; }
    public int Team1Observations { get; set; }
    public int Team2Observations { get; set; }
    public DateTime LastObservationTime { get; set; }
    public int MajorityTeam { get; set; }
    public bool WasTeamSwitched { get; set; }
}

public class TeamVictoryProcessor
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<TeamVictoryProcessor> _logger;

    public TeamVictoryProcessor(PlayerTrackerDbContext dbContext, ILogger<TeamVictoryProcessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Generate team victory achievements for rounds completed since a timestamp.
    /// Awards all players on the winning team based on their last observation.
    /// Only processes rounds that are not active and have RoundTimeRemain >= 0.
    /// Processes in batches for efficiency with large datasets.
    /// </summary>
    public async Task<List<Achievement>> ProcessTeamVictoriesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var allAchievements = new List<Achievement>();
        const int batchSize = 1_000;
        int skip = 0;
        int totalProcessed = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Get batch of completed rounds with team victory conditions
                var rounds = await _dbContext.Rounds.AsNoTracking()
                    .Include(r => r.GameServer)
                    .Where(r => !r.IsActive &&
                                r.EndTime != null &&
                               r.EndTime >= sinceUtc &&
                               !r.IsActive &&
                               r.RoundTimeRemain >= 0 &&
                               r.Tickets1 != null &&
                               r.Tickets2 != null &&
                               r.Tickets1 != r.Tickets2) // Must have a clear winner
                    .OrderBy(r => r.EndTime)
                    .Skip(skip)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                if (rounds.Count == 0)
                {
                    break; // No more rounds to process
                }

                // Get all round IDs for this batch
                var roundIds = rounds.Select(r => r.RoundId).ToList();

                // Get comprehensive player observation analysis for these rounds
                var sql = @"
                    WITH PlayerObservationStats AS (
                        SELECT 
                            ps.RoundId,
                            ps.SessionId,
                            ps.PlayerName,
                            ps.TotalScore,
                            ps.TotalKills,
                            ps.TotalDeaths,
                            ps.LastSeenTime,
                            COUNT(po.ObservationId) as TotalObservations,
                            SUM(CASE WHEN po.Team = 1 THEN 1 ELSE 0 END) as Team1Observations,
                            SUM(CASE WHEN po.Team = 2 THEN 1 ELSE 0 END) as Team2Observations,
                            -- Get final team (last observation)
                            FIRST_VALUE(po.Team) OVER (
                                PARTITION BY ps.SessionId 
                                ORDER BY po.Timestamp DESC
                            ) as FinalTeam,
                            FIRST_VALUE(po.TeamLabel) OVER (
                                PARTITION BY ps.SessionId 
                                ORDER BY po.Timestamp DESC
                            ) as FinalTeamLabel,
                            -- Get the timestamp of the last observation
                            MAX(po.Timestamp) as LastObservationTime
                        FROM PlayerSessions ps
                        INNER JOIN Players p ON ps.PlayerName = p.Name
                        INNER JOIN PlayerObservations po ON ps.SessionId = po.SessionId
                        WHERE ps.RoundId IN ({0}) AND p.AiBot = 0
                        GROUP BY ps.RoundId, ps.SessionId, ps.PlayerName, ps.TotalScore, ps.TotalKills, ps.TotalDeaths, ps.LastSeenTime
                    )
                    SELECT DISTINCT
                        RoundId,
                        SessionId,
                        PlayerName,
                        TotalScore,
                        TotalKills,
                        TotalDeaths,
                        LastSeenTime,
                        FinalTeam,
                        FinalTeamLabel,
                        TotalObservations,
                        Team1Observations,
                        Team2Observations,
                        LastObservationTime,
                        CASE 
                            WHEN Team1Observations > Team2Observations THEN 1
                            WHEN Team2Observations > Team1Observations THEN 2
                            ELSE FinalTeam
                        END as MajorityTeam,
                        CASE 
                            WHEN Team1Observations > 0 AND Team2Observations > 0 THEN 1
                            ELSE 0
                        END as WasTeamSwitched
                    FROM PlayerObservationStats";

                var roundIdParams = string.Join(",", roundIds.Select((_, i) => $"@p{i}"));
                var fullSql = sql.Replace("{0}", roundIdParams);

                var parameters = roundIds.Select((id, i) => new Microsoft.Data.Sqlite.SqliteParameter($"@p{i}", id)).ToArray();

                var playersInRounds = await _dbContext.Database
                    .SqlQueryRaw<PlayerObservationAnalysis>(fullSql, parameters)
                    .ToListAsync(cancellationToken);

                // Group players by round
                var playersByRound = playersInRounds
                    .GroupBy(p => p.RoundId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Get server names for this batch
                var serverGuids = rounds.Select(r => r.ServerGuid).Distinct().ToList();
                var serverNamesByGuid = await _dbContext.Servers.AsNoTracking()
                    .Where(s => serverGuids.Contains(s.Guid))
                    .ToDictionaryAsync(s => s.Guid, s => s.Name, cancellationToken);

                // Process achievements for this batch
                var batchAchievements = ProcessRoundBatch(rounds, playersByRound, serverNamesByGuid, now);
                allAchievements.AddRange(batchAchievements);

                totalProcessed += rounds.Count;
                skip += batchSize;

                _logger.LogDebug("Processed batch of {BatchCount} rounds, total processed: {TotalProcessed}, achievements generated: {AchievementCount}",
                    rounds.Count, totalProcessed, batchAchievements.Count);

                // If we got fewer rounds than batch size, we're done
                if (rounds.Count < batchSize)
                {
                    break;
                }
            }

            _logger.LogInformation("Generated {Count} team victory achievements from {TotalRounds} rounds since {Since}",
                allAchievements.Count, totalProcessed, sinceUtc);
            return allAchievements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing team victories since {Since}", sinceUtc);
            throw;
        }
    }

    /// <summary>
    /// Process a batch of rounds with their players into team victory achievements
    /// </summary>
    private List<Achievement> ProcessRoundBatch(
        List<Round> rounds,
        Dictionary<string, List<PlayerObservationAnalysis>> playersByRound,
        Dictionary<string, string> serverNamesByGuid,
        DateTime processedAt)
    {
        var achievements = new List<Achievement>();
        int roundsProcessed = 0;
        int roundsSkipped = 0;

        foreach (var round in rounds)
        {
            if (!playersByRound.TryGetValue(round.RoundId, out var playersInRound))
            {
                roundsSkipped++;
                continue; // No players found for this round
            }

            // Determine winning team based on tickets
            if (!round.Tickets1.HasValue || !round.Tickets2.HasValue)
            {
                roundsSkipped++;
                continue; // This shouldn't happen due to our query filter, but be safe
            }

            int winningTeam, losingTeam;
            int winningTickets, losingTickets;
            string? winningTeamLabel, losingTeamLabel;

            if (round.Tickets1.Value > round.Tickets2.Value)
            {
                winningTeam = 1;
                losingTeam = 2;
                winningTickets = round.Tickets1.Value;
                losingTickets = round.Tickets2.Value;
                winningTeamLabel = round.Team1Label;
                losingTeamLabel = round.Team2Label;
            }
            else if (round.Tickets2.Value > round.Tickets1.Value)
            {
                winningTeam = 2;
                losingTeam = 1;
                winningTickets = round.Tickets2.Value;
                losingTickets = round.Tickets1.Value;
                winningTeamLabel = round.Team2Label;
                losingTeamLabel = round.Team1Label;
            }
            else
            {
                // Draw - no team victory achievements
                roundsSkipped++;
                _logger.LogDebug("Round {RoundId} ended in a draw ({Tickets1} - {Tickets2}), no team victory achievements awarded",
                    round.RoundId, round.Tickets1.Value, round.Tickets2.Value);
                continue;
            }

            // Filter players who were active within 2 minutes of round end
            var twoMinutesBeforeEnd = (round.EndTime ?? DateTime.UtcNow).AddMinutes(-2);
            var eligiblePlayers = playersInRound
                .Where(p => p.LastObservationTime >= twoMinutesBeforeEnd)
                .ToList();

            if (eligiblePlayers.Count == 0)
            {
                roundsSkipped++;
                _logger.LogWarning("No eligible players found for round {RoundId} (none active within 2 minutes of end)", round.RoundId);
                continue;
            }

            var serverName = serverNamesByGuid.GetValueOrDefault(round.ServerGuid, "");

            // Process regular team victory achievements for players on winning team
            var winningTeamPlayers = eligiblePlayers
                .Where(p => p.FinalTeam == winningTeam)
                .ToList();

            // Process both regular and team-switched achievements if we have winning team players
            if (winningTeamPlayers.Count > 0)
            {
                var medianTeamObservations = CalculateMedianTeamObservations(winningTeamPlayers, winningTeam);

                // Process regular team victory achievements
                foreach (var player in winningTeamPlayers.Where(p => p.TotalObservations > 0))
                {
                    var achievement = CreateTeamVictoryAchievement(
                        player, winningTeam, winningTeamLabel, winningTickets, losingTeam, losingTeamLabel, losingTickets,
                        serverName, round, medianTeamObservations, winningTeamPlayers.Count, processedAt,
                        AchievementTypes.TeamVictory, "Team Victory", isTeamSwitched: false);

                    achievements.Add(achievement);
                }

                // Process team-switched victory achievements
                var teamSwitchedPlayers = eligiblePlayers
                    .Where(p => p.WasTeamSwitched && p.MajorityTeam == winningTeam && p.FinalTeam != winningTeam)
                    .ToList();

                foreach (var player in teamSwitchedPlayers)
                {
                    var achievement = CreateTeamVictoryAchievement(
                        player, winningTeam, winningTeamLabel, winningTickets, losingTeam, losingTeamLabel, losingTickets,
                        serverName, round, medianTeamObservations, winningTeamPlayers.Count, processedAt,
                        AchievementTypes.TeamVictorySwitched, "Team Victory (Team Switched)", isTeamSwitched: true);

                    achievements.Add(achievement);
                }
            }

            if (winningTeamPlayers.Count == 0)
            {
                roundsSkipped++;
                _logger.LogWarning("No achievements generated for round {RoundId} on team {WinningTeam}", round.RoundId, winningTeam);
            }
            else
            {
                roundsProcessed++;
            }
        }

        return achievements;
    }

    /// <summary>
    /// Calculate median team observations for relative comparison baseline
    /// </summary>
    private double CalculateMedianTeamObservations(List<PlayerObservationAnalysis> winningTeamPlayers, int winningTeam)
    {
        var winningTeamObservations = winningTeamPlayers
            .Select(p => winningTeam == 1 ? p.Team1Observations : p.Team2Observations)
            .OrderBy(x => x)
            .ToList();

        var median = winningTeamObservations.Count % 2 == 0
            ? (winningTeamObservations[winningTeamObservations.Count / 2 - 1] + winningTeamObservations[winningTeamObservations.Count / 2]) / 2.0
            : winningTeamObservations[winningTeamObservations.Count / 2];

        // Ensure we have a reasonable baseline (minimum 1 observation)
        return Math.Max(1.0, median);
    }

    /// <summary>
    /// Create a team victory achievement with proper scoring and tier assignment
    /// </summary>
    private Achievement CreateTeamVictoryAchievement(
        PlayerObservationAnalysis player,
        int winningTeam, string? winningTeamLabel, int winningTickets,
        int losingTeam, string? losingTeamLabel, int losingTickets,
        string serverName, Round round, double medianTeamObservations, int winningTeamPlayersCount,
        DateTime processedAt, string achievementId, string achievementName, bool isTeamSwitched)
    {
        var playerTeamObservations = winningTeam == 1 ? player.Team1Observations : player.Team2Observations;

        // Calculate team participation ratio (loyalty factor)
        var teamParticipationRatio = player.TotalObservations > 0
            ? (double)playerTeamObservations / player.TotalObservations
            : (isTeamSwitched ? 0 : 1.0);

        // Calculate contribution relative to median teammate
        var contributionScore = playerTeamObservations / medianTeamObservations;

        // Apply loyalty multiplier
        var finalScore = contributionScore * teamParticipationRatio;

        var metadata = new TeamVictoryMetadata(
            winningTeam,
            winningTeamLabel,
            winningTickets,
            losingTeam,
            losingTeamLabel,
            losingTickets,
            serverName,
            player.TotalScore,
            player.TotalKills,
            player.TotalDeaths,
            round.ParticipantCount,
            teamParticipationRatio,
            contributionScore,
            playerTeamObservations,
            (int)Math.Round(medianTeamObservations),
            winningTeamPlayersCount,
            player.WasTeamSwitched
        );

        // Determine tier based on final score and achievement type
        var tier = isTeamSwitched
            ? finalScore switch
            {
                >= 1.0 => BadgeTiers.Gold,     // Exceptional contribution despite switching
                >= 0.7 => BadgeTiers.Silver,   // Good contribution despite switching
                _ => BadgeTiers.Bronze         // Recognition for majority time on winning team
            }
            : finalScore switch
            {
                >= 1.2 => BadgeTiers.Legend,   // 120%+ of median with high loyalty
                >= 1.0 => BadgeTiers.Gold,     // At/above median with good loyalty  
                >= 0.7 => BadgeTiers.Silver,   // 70%+ of median
                >= 0.4 => BadgeTiers.Bronze,   // 40%+ of median
                _ => BadgeTiers.Bronze         // Everyone gets at least Bronze
            };

        var achievedAt = round.EndTime ?? player.LastSeenTime;

        return new Achievement
        {
            PlayerName = player.PlayerName,
            AchievementType = AchievementTypes.TeamVictory,
            AchievementId = achievementId,
            AchievementName = achievementName,
            Tier = tier,
            Value = (uint)Math.Round(finalScore * 100), // Store final score as percentage
            AchievedAt = achievedAt,
            ProcessedAt = processedAt,
            ServerGuid = round.ServerGuid,
            MapName = round.MapName,
            RoundId = round.RoundId,
            Metadata = JsonSerializer.Serialize(metadata),
            Game = round.GameServer?.Game ?? "unknown",
            Version = achievedAt  // Use achieved_at as deterministic version for idempotency
        };
    }
}
