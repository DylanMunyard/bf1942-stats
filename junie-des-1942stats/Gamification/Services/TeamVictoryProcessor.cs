using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

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
        using var activity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.ProcessTeamVictoriesSinceAsync");
        activity?.SetTag("since_utc", sinceUtc.ToString("O"));

        var now = DateTime.UtcNow;
        var allAchievements = new List<Achievement>();
        const int batchSize = 1_000;
        int skip = 0;
        int totalProcessed = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var batchActivity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.ProcessBatch");
                batchActivity?.SetTag("batch_size", batchSize);
                batchActivity?.SetTag("skip", skip);

                // Get batch of completed rounds with team victory conditions
                var rounds = await _dbContext.Rounds.AsNoTracking()
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
                    batchActivity?.SetTag("rounds_found", 0);
                    break; // No more rounds to process
                }

                batchActivity?.SetTag("rounds_found", rounds.Count);

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

                using var sqlActivity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.GetPlayerObservations");
                sqlActivity?.SetTag("round_count", rounds.Count);

                var playersInRounds = await _dbContext.Database
                    .SqlQueryRaw<PlayerObservationAnalysis>(fullSql, parameters)
                    .ToListAsync(cancellationToken);

                sqlActivity?.SetTag("players_found", playersInRounds.Count);

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
                using var processingActivity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.ProcessRoundBatch");
                processingActivity?.SetTag("rounds_to_process", rounds.Count);
                processingActivity?.SetTag("total_players", playersInRounds.Count);

                var batchAchievements = ProcessRoundBatch(rounds, playersByRound, serverNamesByGuid, now);

                processingActivity?.SetTag("achievements_generated", batchAchievements.Count);
                batchActivity?.SetTag("achievements_generated", batchAchievements.Count);
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

            activity?.SetTag("total_achievements_generated", allAchievements.Count);
            activity?.SetTag("total_rounds_processed", totalProcessed);

            _logger.LogInformation("Generated {Count} team victory achievements from {TotalRounds} rounds since {Since}",
                allAchievements.Count, totalProcessed, sinceUtc);
            return allAchievements;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Team victory processing failed: {ex.Message}");
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
        using var activity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.ProcessRoundBatch");
        activity?.SetTag("rounds_count", rounds.Count);

        var achievements = new List<Achievement>();
        int roundsProcessed = 0;
        int roundsSkipped = 0;

        foreach (var round in rounds)
        {
            using var roundActivity = ActivitySources.Gamification.StartActivity("TeamVictoryProcessor.ProcessRound");
            roundActivity?.SetTag("round_id", round.RoundId);
            roundActivity?.SetTag("server_guid", round.ServerGuid);
            roundActivity?.SetTag("map_name", round.MapName);

            if (!playersByRound.TryGetValue(round.RoundId, out var playersInRound))
            {
                roundsSkipped++;
                roundActivity?.SetTag("skipped_reason", "no_players");
                continue; // No players found for this round
            }

            roundActivity?.SetTag("players_in_round", playersInRound.Count);

            // Determine winning team based on tickets
            if (!round.Tickets1.HasValue || !round.Tickets2.HasValue)
            {
                roundsSkipped++;
                roundActivity?.SetTag("skipped_reason", "missing_tickets");
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
                roundActivity?.SetTag("skipped_reason", "draw");
                roundActivity?.SetTag("tickets1", round.Tickets1.Value);
                roundActivity?.SetTag("tickets2", round.Tickets2.Value);
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
                roundActivity?.SetTag("skipped_reason", "no_eligible_players");
                _logger.LogWarning("No eligible players found for round {RoundId} (none active within 2 minutes of end)", round.RoundId);
                continue;
            }

            roundActivity?.SetTag("eligible_players", eligiblePlayers.Count);
            roundActivity?.SetTag("winning_team", winningTeam);
            roundActivity?.SetTag("winning_tickets", winningTickets);
            roundActivity?.SetTag("losing_tickets", losingTickets);

            var serverName = serverNamesByGuid.GetValueOrDefault(round.ServerGuid, "");

            // Process regular team victory achievements for players on winning team
            var winningTeamPlayers = eligiblePlayers
                .Where(p => p.FinalTeam == winningTeam)
                .ToList();

            // Process both regular and team-switched achievements if we have winning team players
            if (winningTeamPlayers.Count > 0)
            {
                roundActivity?.SetTag("winning_team_players", winningTeamPlayers.Count);

                var medianTeamObservations = CalculateMedianTeamObservations(winningTeamPlayers, winningTeam);
                roundActivity?.SetTag("median_team_observations", medianTeamObservations);

                // Process regular team victory achievements
                var regularAchievements = 0;
                foreach (var player in winningTeamPlayers.Where(p => p.TotalObservations > 0))
                {
                    var achievement = CreateTeamVictoryAchievement(
                        player, winningTeam, winningTeamLabel, winningTickets, losingTeam, losingTeamLabel, losingTickets,
                        serverName, round, medianTeamObservations, winningTeamPlayers.Count, processedAt,
                        "team_victory", "Team Victory", isTeamSwitched: false);

                    achievements.Add(achievement);
                    regularAchievements++;
                }
                roundActivity?.SetTag("regular_achievements", regularAchievements);

                // Process team-switched victory achievements
                var teamSwitchedPlayers = eligiblePlayers
                    .Where(p => p.WasTeamSwitched && p.MajorityTeam == winningTeam && p.FinalTeam != winningTeam)
                    .ToList();

                var teamSwitchedAchievements = 0;
                foreach (var player in teamSwitchedPlayers)
                {
                    var achievement = CreateTeamVictoryAchievement(
                        player, winningTeam, winningTeamLabel, winningTickets, losingTeam, losingTeamLabel, losingTickets,
                        serverName, round, medianTeamObservations, winningTeamPlayers.Count, processedAt,
                        "team_victory_switched", "Team Victory (Team Switched)", isTeamSwitched: true);

                    achievements.Add(achievement);
                    teamSwitchedAchievements++;
                }
                roundActivity?.SetTag("team_switched_players", teamSwitchedPlayers.Count);
                roundActivity?.SetTag("team_switched_achievements", teamSwitchedAchievements);
            }

            if (winningTeamPlayers.Count == 0)
            {
                roundsSkipped++;
                roundActivity?.SetTag("skipped_reason", "no_winning_team_players");
                _logger.LogWarning("No achievements generated for round {RoundId} on team {WinningTeam}", round.RoundId, winningTeam);
            }
            else
            {
                roundsProcessed++;
            }
        }

        activity?.SetTag("rounds_processed", roundsProcessed);
        activity?.SetTag("rounds_skipped", roundsSkipped);
        activity?.SetTag("total_achievements", achievements.Count);

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
            Metadata = JsonSerializer.Serialize(metadata)
        };
    }
}