using api.Gamification.Models;
using api.Analytics.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public class KillStreakDetector(
    SqliteGamificationService readService,
    BadgeDefinitionsService badgeService,
    PlayerTrackerDbContext dbContext,
    ILogger<KillStreakDetector> logger)
{

    public async Task<List<Achievement>> CalculateKillStreaksForRoundAsync(PlayerRound round)
    {
        return await CalculateKillStreaksFromPlayerMetricsAsync(round);
    }


    public async Task<KillStreakStats> GetPlayerKillStreakStatsAsync(string playerName)
    {
        try
        {
            // Get all kill streak achievements for this player
            var streakAchievements = await readService.GetPlayerAchievementsByTypeAsync(
                playerName, AchievementTypes.KillStreak);

            if (!streakAchievements.Any())
            {
                return new KillStreakStats();
            }

            // Find the best streak
            var bestAchievement = streakAchievements.OrderByDescending(a => a.Value).First();

            return new KillStreakStats
            {
                BestSingleRoundStreak = (int)bestAchievement.Value,
                BestStreakMap = bestAchievement.MapName,
                BestStreakServer = bestAchievement.ServerGuid, // Use ServerGuid directly instead of looking up name
                BestStreakDate = bestAchievement.AchievedAt,
                RecentStreaks = streakAchievements
                    .OrderByDescending(a => a.AchievedAt)
                    .Take(10)
                    .Select(a => new KillStreak
                    {
                        PlayerName = a.PlayerName,
                        StreakCount = (int)a.Value,
                        StreakStart = a.AchievedAt,
                        StreakEnd = a.AchievedAt,
                        ServerGuid = a.ServerGuid,
                        MapName = a.MapName,
                        RoundId = a.RoundId,
                        IsActive = false
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting kill streak stats for player {PlayerName}", playerName);
            return new KillStreakStats();
        }
    }


    public async Task<List<Achievement>> ProcessKillStreaksForRoundsAsync(List<PlayerRound> rounds)
    {
        var allAchievements = new List<Achievement>();

        foreach (var round in rounds)
        {
            var roundAchievements = await CalculateKillStreaksForRoundAsync(round);
            allAchievements.AddRange(roundAchievements);
        }

        if (allAchievements.Any())
        {
            logger.LogInformation("Processed {RoundCount} rounds, found {AchievementCount} kill streak achievements",
                rounds.Count, allAchievements.Count);
        }

        return allAchievements;
    }

    /// <summary>
    /// Calculate kill streaks using player observation data.
    /// This provides accurate and granular streak detection.
    /// </summary>
    private async Task<List<Achievement>> CalculateKillStreaksFromPlayerMetricsAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Get player metrics for the round timeframe
            var playerMetrics = await GetPlayerMetricsForRoundAsync(round);

            if (playerMetrics.Count < 2)
            {
                logger.LogDebug("Insufficient player_metrics data for {PlayerName} round {RoundId} - found {Count} metrics",
                    round.PlayerName, round.RoundId, playerMetrics.Count);
                return achievements; // Need at least 2 metrics to detect streaks
            }

            // Calculate streaks from the more granular player_metrics data
            var streakInstances = CalculateAllStreakInstancesFromMetrics(playerMetrics);

            if (!streakInstances.Any()) return achievements; // No streaks of 5+ kills

            // Get existing achievements to avoid duplicates
            var existingAchievements = await readService.GetPlayerAchievementsByTypeAsync(round.PlayerName, AchievementTypes.KillStreak);
            var existingRoundAchievements = existingAchievements.Where(a => a.RoundId == round.RoundId).ToList();

            foreach (var streakInstance in streakInstances)
            {
                var achievementId = $"kill_streak_{streakInstance.Threshold}";

                // Check if player already has this specific achievement instance
                var hasAchievementForThisInstance = existingRoundAchievements.Any(a =>
                    a.AchievementId == achievementId &&
                    Math.Abs((a.AchievedAt - streakInstance.AchievedAt).TotalMinutes) < 2); // Allow 2-minute tolerance

                if (!hasAchievementForThisInstance)
                {
                    var badgeDefinition = badgeService.GetBadgeDefinition(achievementId);
                    if (badgeDefinition != null)
                    {
                        achievements.Add(new Achievement
                        {
                            PlayerName = round.PlayerName,
                            AchievementType = AchievementTypes.KillStreak,
                            AchievementId = achievementId,
                            AchievementName = badgeDefinition.Name,
                            Tier = badgeDefinition.Tier,
                            Value = (uint)streakInstance.Threshold,
                            AchievedAt = streakInstance.AchievedAt,
                            ProcessedAt = DateTime.UtcNow,
                            ServerGuid = round.ServerGuid,
                            MapName = round.MapName,
                            RoundId = round.RoundId,
                            Metadata = $"{{\"actual_streak\":{streakInstance.Threshold},\"round_kills\":{round.FinalKills}}}",
                            Game = round.Game ?? "unknown",
                            Version = streakInstance.AchievedAt  // Use achieved_at as deterministic version for idempotency
                        });

                        logger.LogInformation("Kill streak achievement: {PlayerName} achieved {AchievementName} with {Threshold} kills at {AchievementTime}",
                            round.PlayerName, badgeDefinition.Name, streakInstance.Threshold, streakInstance.AchievedAt);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calculating kill streaks from player_metrics for round {RoundId}, player {PlayerName}",
                round.RoundId, round.PlayerName);
        }

        return achievements;
    }

    /// <summary>
    /// Get player metrics data for a round using SQLite PlayerObservations.
    /// </summary>
    private async Task<List<PlayerMetric>> GetPlayerMetricsForRoundAsync(PlayerRound round)
    {
        try
        {
            var roundStartTime = round.RoundStartTime;
            var roundEndTime = round.RoundEndTime;

            // Add some buffer to account for timing differences
            var bufferMinutes = 2;
            var searchStartTime = roundStartTime.AddMinutes(-bufferMinutes);
            var searchEndTime = roundEndTime.AddMinutes(bufferMinutes);

            // Query PlayerObservations from SQLite for this player's session
            var observations = await dbContext.PlayerObservations
                .Where(po => po.Session.PlayerName == round.PlayerName &&
                            po.Session.ServerGuid == round.ServerGuid &&
                            po.Session.MapName == round.MapName &&
                            po.Timestamp >= searchStartTime &&
                            po.Timestamp <= searchEndTime)
                .OrderBy(po => po.Timestamp)
                .Select(po => new PlayerMetric
                {
                    Timestamp = po.Timestamp,
                    Kills = (ushort)po.Kills,
                    Deaths = (ushort)po.Deaths,
                    PlayerName = round.PlayerName
                })
                .ToListAsync();

            logger.LogDebug("Retrieved {Count} player observations for {PlayerName} round {RoundId}",
                observations.Count, round.PlayerName, round.RoundId);

            return observations;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching player observations for round {RoundId}, player {PlayerName}",
                round.RoundId, round.PlayerName);
            return [];
        }
    }

    /// <summary>
    /// Calculate all streak instances from player metrics data
    /// This tracks multiple instances of the same threshold being achieved in a round
    /// </summary>
    private List<StreakInstance> CalculateAllStreakInstancesFromMetrics(List<PlayerMetric> metrics)
    {
        var streakInstances = new List<StreakInstance>();
        var thresholdValues = new[] { 5, 10, 15, 20, 25, 30, 50 };

        if (metrics.Count < 2) return streakInstances;

        int currentStreak = 0;
        var achievedThresholdsInCurrentStreak = new HashSet<int>();
        ushort previousKills = metrics.First().Kills;
        ushort previousDeaths = metrics.First().Deaths;

        foreach (var metric in metrics.Skip(1))
        {
            var killsDiff = metric.Kills - previousKills;
            var deathsDiff = metric.Deaths - previousDeaths;

            if (killsDiff > 0 && deathsDiff == 0)
            {
                // Player got kills without dying - continue/start streak
                currentStreak += killsDiff;

                // Check if any thresholds were reached during this metric
                foreach (var threshold in thresholdValues)
                {
                    if (currentStreak >= threshold && !achievedThresholdsInCurrentStreak.Contains(threshold))
                    {
                        // This threshold was reached for this streak
                        streakInstances.Add(new StreakInstance
                        {
                            Threshold = threshold,
                            AchievedAt = metric.Timestamp
                        });
                        achievedThresholdsInCurrentStreak.Add(threshold);
                    }
                }
            }
            else if (deathsDiff > 0)
            {
                // Player died - reset streak and clear achieved thresholds for next streak
                currentStreak = 0;
                achievedThresholdsInCurrentStreak.Clear();
            }

            previousKills = metric.Kills;
            previousDeaths = metric.Deaths;
        }

        return streakInstances;
    }

    /// <summary>
    /// Simple PlayerMetric class for streak calculations
    /// </summary>
    private class PlayerMetric
    {
        public DateTime Timestamp { get; set; }
        public ushort Kills { get; set; }
        public ushort Deaths { get; set; }
        public string PlayerName { get; set; } = "";
    }

    /// <summary>
    /// Represents a specific instance of a kill streak threshold being achieved
    /// </summary>
    private class StreakInstance
    {
        public int Threshold { get; set; }
        public DateTime AchievedAt { get; set; }
    }
}
