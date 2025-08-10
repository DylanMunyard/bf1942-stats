using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace junie_des_1942stats.Gamification.Services;

public class PerformanceBadgeCalculator
{
    private readonly ClickHouseGamificationService _readService;
    private readonly BadgeDefinitionsService _badgeService;
    private readonly ILogger<PerformanceBadgeCalculator> _logger;

    public PerformanceBadgeCalculator(
        ClickHouseGamificationService readService,
        BadgeDefinitionsService badgeService,
        ILogger<PerformanceBadgeCalculator> logger)
    {
        _readService = readService;
        _badgeService = badgeService;
        _logger = logger;
    }

    public async Task<List<Achievement>> CheckPerformanceBadgesAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Check KPM-based badges
            var kpmAchievements = await CheckKPMBadgesAsync(round);
            achievements.AddRange(kpmAchievements);

            // Check KD ratio badges
            var kdAchievements = await CheckKDRatioBadgesAsync(round);
            achievements.AddRange(kdAchievements);

            // Map mastery badges are calculated less frequently due to complexity
            // These would typically be calculated in a separate background process
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking performance badges for player {PlayerName}, round {RoundId}",
                round.PlayerName, round.RoundId);
        }

        return achievements;
    }

    private async Task<List<Achievement>> CheckKPMBadgesAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Use ClickHouse player_metrics for accurate calculation
            var performanceStats = await _readService.CalculatePlayerKPMStatsAsync(round.PlayerName, 10);

            if (performanceStats.RoundsAnalyzed < 10)
            {
                _logger.LogDebug("Insufficient data for KPM calculation for {PlayerName}: {Rounds} rounds",
                    round.PlayerName, performanceStats.RoundsAnalyzed);
                return achievements;
            }

            var kmp = performanceStats.KillsPerMinute;
            var roundsAnalyzed = performanceStats.RoundsAnalyzed;
            var totalKills = (uint)performanceStats.TotalKills;
            var totalMinutes = performanceStats.TotalMinutes;

            _logger.LogDebug("ClickHouse KPM calculation for {PlayerName}: {KPM:F2} over {Rounds} rounds",
                round.PlayerName, kmp, roundsAnalyzed);

            // Check KPM badge thresholds
            var kpmBadges = new[]
            {
                ("sharpshooter_legend", 2.5, 100),
                ("sharpshooter_gold", 2.0, 50),
                ("sharpshooter_silver", 1.5, 25),
                ("sharpshooter_bronze", 1.0, 10)
            };

            foreach (var (badgeId, minKpm, minRounds) in kpmBadges)
            {
                if (kmp >= minKpm && roundsAnalyzed >= minRounds)
                {
                    // Check if player already has this badge
                    var hasBadge = await _readService.PlayerHasAchievementAsync(round.PlayerName, badgeId);
                    if (!hasBadge)
                    {
                        var badgeDefinition = _badgeService.GetBadgeDefinition(badgeId);
                        if (badgeDefinition != null)
                        {
                            achievements.Add(new Achievement
                            {
                                PlayerName = round.PlayerName,
                                AchievementType = AchievementTypes.Badge,
                                AchievementId = badgeId,
                                AchievementName = badgeDefinition.Name,
                                Tier = badgeDefinition.Tier,
                                Value = (uint)(kmp * 100), // Store as integer (KPM * 100)
                                AchievedAt = round.RoundEndTime,
                                ProcessedAt = DateTime.UtcNow,
                                ServerGuid = round.ServerGuid,
                                MapName = round.MapName,
                                RoundId = round.RoundId,
                                Metadata = $"{{\"kpm\":{kmp:F2},\"rounds_analyzed\":{roundsAnalyzed},\"total_kills\":{totalKills},\"total_minutes\":{totalMinutes:F1}}}"
                            });

                            _logger.LogInformation("KPM badge achieved: {PlayerName} earned {BadgeName} with {KPM:F2} KPM over {Rounds} rounds",
                                round.PlayerName, badgeDefinition.Name, kmp, roundsAnalyzed);
                        }

                        // Only award the highest tier achieved
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking KPM badges for player {PlayerName}", round.PlayerName);
        }

        return achievements;
    }

    private async Task<List<Achievement>> CheckKDRatioBadgesAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Use ClickHouse player_metrics for accurate calculation
            var performanceStats = await _readService.CalculatePlayerKPMStatsAsync(round.PlayerName, 25);

            if (performanceStats.RoundsAnalyzed < 25)
            {
                _logger.LogDebug("Insufficient data for KD calculation for {PlayerName}: {Rounds} rounds",
                    round.PlayerName, performanceStats.RoundsAnalyzed);
                return achievements;
            }

            var kdRatio = performanceStats.KillDeathRatio;
            var roundsAnalyzed = performanceStats.RoundsAnalyzed;
            var totalKills = performanceStats.TotalKills;
            var totalDeaths = performanceStats.TotalDeaths;

            if (totalDeaths <= 0)
            {
                _logger.LogDebug("No deaths recorded for {PlayerName}, skipping KD calculation", round.PlayerName);
                return achievements;
            }

            _logger.LogDebug("ClickHouse KD calculation for {PlayerName}: {KD:F2} over {Rounds} rounds",
                round.PlayerName, kdRatio, roundsAnalyzed);

            // Check KD ratio badge thresholds
            var kdBadges = new[]
            {
                ("elite_warrior_legend", 5.0, 200),
                ("elite_warrior_gold", 4.0, 100),
                ("elite_warrior_silver", 3.0, 50),
                ("elite_warrior_bronze", 2.0, 25)
            };

            foreach (var (badgeId, minKd, minRounds) in kdBadges)
            {
                if (kdRatio >= minKd && roundsAnalyzed >= minRounds)
                {
                    // Check if player already has this badge
                    var hasBadge = await _readService.PlayerHasAchievementAsync(round.PlayerName, badgeId);
                    if (!hasBadge)
                    {
                        var badgeDefinition = _badgeService.GetBadgeDefinition(badgeId);
                        if (badgeDefinition != null)
                        {
                            achievements.Add(new Achievement
                            {
                                PlayerName = round.PlayerName,
                                AchievementType = AchievementTypes.Badge,
                                AchievementId = badgeId,
                                AchievementName = badgeDefinition.Name,
                                Tier = badgeDefinition.Tier,
                                Value = (uint)(kdRatio * 100), // Store as integer (KD * 100)
                                AchievedAt = round.RoundEndTime,
                                ProcessedAt = DateTime.UtcNow,
                                ServerGuid = round.ServerGuid,
                                MapName = round.MapName,
                                RoundId = round.RoundId,
                                Metadata = $"{{\"kd_ratio\":{kdRatio:F2},\"rounds_analyzed\":{roundsAnalyzed},\"total_kills\":{totalKills},\"total_deaths\":{totalDeaths}}}"
                            });

                            _logger.LogInformation("KD badge achieved: {PlayerName} earned {BadgeName} with {KDRatio:F2} KD over {Rounds} rounds",
                                round.PlayerName, badgeDefinition.Name, kdRatio, roundsAnalyzed);
                        }

                        // Only award the highest tier achieved
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking KD badges for player {PlayerName}", round.PlayerName);
        }

        return achievements;
    }

    /// <summary>
    /// Calculate consistency badge - checks if player has positive KD in X% of recent rounds
    /// This is calculated less frequently than per-round badges
    /// </summary>
    public async Task<List<Achievement>> CheckConsistencyBadgeAsync(string playerName)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Use ClickHouse player_metrics for accurate round detection
            var recentPerformance = await _readService.GetPlayerRecentPerformanceAsync(playerName, 50);

            if (recentPerformance.Count < 50)
            {
                _logger.LogDebug("Insufficient data for consistency calculation for {PlayerName}: {Rounds} rounds",
                    playerName, recentPerformance.Count);
                return achievements;
            }

            var positiveKdRounds = recentPerformance.Count(r => r.Kills >= r.Deaths);
            var positivePercentage = (double)positiveKdRounds / recentPerformance.Count * 100;
            var totalRoundsAnalyzed = recentPerformance.Count;

            // Get the latest PlayerRound for achievement context
            var recentPlayerRounds = await _readService.GetPlayerRecentRoundsAsync(playerName, 1);
            if (!recentPlayerRounds.Any())
            {
                _logger.LogWarning("No PlayerRounds found for consistency badge context for {PlayerName}", playerName);
                return achievements;
            }

            var latestRound = recentPlayerRounds.First();

            _logger.LogDebug("ClickHouse consistency calculation for {PlayerName}: {Percentage:F1}% over {Rounds} rounds",
                playerName, positivePercentage, totalRoundsAnalyzed);

            if (positivePercentage >= 80)
            {
                var badgeId = "consistent_killer";
                var hasBadge = await _readService.PlayerHasAchievementAsync(playerName, badgeId);

                if (!hasBadge)
                {
                    var badgeDefinition = _badgeService.GetBadgeDefinition(badgeId);
                    if (badgeDefinition != null)
                    {
                        achievements.Add(new Achievement
                        {
                            PlayerName = playerName,
                            AchievementType = AchievementTypes.Badge,
                            AchievementId = badgeId,
                            AchievementName = badgeDefinition.Name,
                            Tier = badgeDefinition.Tier,
                            Value = (uint)positivePercentage,
                            AchievedAt = latestRound.RoundEndTime,
                            ProcessedAt = DateTime.UtcNow,
                            ServerGuid = latestRound.ServerGuid,
                            MapName = latestRound.MapName,
                            RoundId = latestRound.RoundId,
                            Metadata = $"{{\"positive_percentage\":{positivePercentage:F1},\"positive_rounds\":{positiveKdRounds},\"total_rounds\":{totalRoundsAnalyzed}}}"
                        });

                        _logger.LogInformation("Consistency badge achieved: {PlayerName} had positive KD in {Percentage:F1}% of last {Rounds} rounds",
                            playerName, positivePercentage, totalRoundsAnalyzed);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking consistency badge for player {PlayerName}", playerName);
        }

        return achievements;
    }

    /// <summary>
    /// Calculate map mastery badges - this should be run periodically, not per-round
    /// </summary>
    public Task<List<Achievement>> CheckMapMasteryBadgesAsync(string playerName, string mapName)
    {
        var achievements = new List<Achievement>();

        try
        {
            // This would require complex percentile calculations against all players
            // For now, this is a placeholder for the more complex calculation
            // that would typically be done in a background service

            _logger.LogDebug("Map mastery badge calculation not yet implemented for {PlayerName} on {MapName}",
                playerName, mapName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking map mastery badges for player {PlayerName} on map {MapName}",
                playerName, mapName);
        }

        return Task.FromResult(achievements);
    }

    public List<BadgeDefinition> GetAvailablePerformanceBadges()
    {
        return _badgeService.GetBadgesByCategory(BadgeCategories.Performance);
    }

    /// <summary>
    /// Batch processing for multiple rounds - more efficient than processing individually
    /// </summary>
    public async Task<List<Achievement>> ProcessPerformanceBadgesBatchAsync(List<PlayerRound> rounds)
    {
        var allAchievements = new List<Achievement>();

        try
        {
            // Group rounds by player for efficient processing
            var roundsByPlayer = rounds.GroupBy(r => r.PlayerName).ToList();

            _logger.LogDebug("Processing performance badges for {PlayerCount} players with {RoundCount} total rounds",
                roundsByPlayer.Count, rounds.Count);

            foreach (var playerGroup in roundsByPlayer)
            {
                var playerName = playerGroup.Key;
                var playerRounds = playerGroup.OrderByDescending(r => r.RoundEndTime).ToList();
                var latestRound = playerRounds.First();

                try
                {
                    // Process KPM badges once per player (not per round)
                    var kmpAchievements = await CheckKPMBadgesAsync(latestRound);
                    allAchievements.AddRange(kmpAchievements);

                    // Process KD ratio badges once per player
                    var kdAchievements = await CheckKDRatioBadgesAsync(latestRound);
                    allAchievements.AddRange(kdAchievements);

                    if (kmpAchievements.Any() || kdAchievements.Any())
                    {
                        _logger.LogDebug("Player {PlayerName}: {KmpCount} KPM + {KdCount} KD achievements",
                            playerName, kmpAchievements.Count, kdAchievements.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing performance badges for player {PlayerName}", playerName);
                }
            }

            if (allAchievements.Any())
            {
                _logger.LogInformation("Batch processed performance badges: {AchievementCount} achievements for {PlayerCount} players",
                    allAchievements.Count, roundsByPlayer.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch performance badge processing");
        }

        return allAchievements;
    }
}