using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class PerformanceBadgeCalculator
{
    private readonly ClickHouseGamificationService _clickHouseService;
    private readonly BadgeDefinitionsService _badgeService;
    private readonly ILogger<PerformanceBadgeCalculator> _logger;

    public PerformanceBadgeCalculator(
        ClickHouseGamificationService clickHouseService,
        BadgeDefinitionsService badgeService,
        ILogger<PerformanceBadgeCalculator> logger)
    {
        _clickHouseService = clickHouseService;
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
            // Get player's last 50 rounds for KPM calculation
            var recentRounds = await _clickHouseService.GetPlayerRecentRoundsAsync(round.PlayerName, 50);
            
            if (recentRounds.Count < 10) return achievements; // Need minimum rounds

            var totalKills = recentRounds.Sum(r => r.Kills);
            var totalMinutes = recentRounds.Sum(r => r.PlayTimeMinutes);
            
            if (totalMinutes <= 0) return achievements;

            var kpm = totalKills / totalMinutes;

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
                if (kpm >= minKpm && recentRounds.Count >= minRounds)
                {
                    // Check if player already has this badge
                    var hasBadge = await _clickHouseService.PlayerHasAchievementAsync(round.PlayerName, badgeId);
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
                                Value = (uint)(kpm * 100), // Store as integer (KPM * 100)
                                AchievedAt = round.RoundEndTime,
                                ProcessedAt = DateTime.UtcNow,
                                ServerGuid = round.ServerGuid,
                                MapName = round.MapName,
                                RoundId = round.RoundId,
                                Metadata = $"{{\"kpm\":{kpm:F2},\"rounds_analyzed\":{recentRounds.Count},\"total_kills\":{totalKills},\"total_minutes\":{totalMinutes:F1}}}"
                            });

                            _logger.LogInformation("KPM badge achieved: {PlayerName} earned {BadgeName} with {KPM:F2} KPM over {Rounds} rounds",
                                round.PlayerName, badgeDefinition.Name, kpm, recentRounds.Count);
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
            // Get player's last 100 rounds for KD calculation
            var recentRounds = await _clickHouseService.GetPlayerRecentRoundsAsync(round.PlayerName, 100);
            
            if (recentRounds.Count < 25) return achievements; // Need minimum rounds

            var totalKills = recentRounds.Sum(r => r.Kills);
            var totalDeaths = recentRounds.Sum(r => r.Deaths);
            
            if (totalDeaths <= 0) return achievements; // Avoid division by zero

            var kdRatio = (double)totalKills / totalDeaths;

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
                if (kdRatio >= minKd && recentRounds.Count >= minRounds)
                {
                    // Check if player already has this badge
                    var hasBadge = await _clickHouseService.PlayerHasAchievementAsync(round.PlayerName, badgeId);
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
                                Metadata = $"{{\"kd_ratio\":{kdRatio:F2},\"rounds_analyzed\":{recentRounds.Count},\"total_kills\":{totalKills},\"total_deaths\":{totalDeaths}}}"
                            });

                            _logger.LogInformation("KD badge achieved: {PlayerName} earned {BadgeName} with {KDRatio:F2} KD over {Rounds} rounds",
                                round.PlayerName, badgeDefinition.Name, kdRatio, recentRounds.Count);
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
            var recentRounds = await _clickHouseService.GetPlayerRecentRoundsAsync(playerName, 50);
            
            if (recentRounds.Count < 50) return achievements;

            var positiveKdRounds = recentRounds.Count(r => r.Kills >= r.Deaths);
            var positivePercentage = (double)positiveKdRounds / recentRounds.Count * 100;

            if (positivePercentage >= 80)
            {
                var badgeId = "consistent_killer";
                var hasBadge = await _clickHouseService.PlayerHasAchievementAsync(playerName, badgeId);
                
                if (!hasBadge)
                {
                    var badgeDefinition = _badgeService.GetBadgeDefinition(badgeId);
                    if (badgeDefinition != null)
                    {
                        var latestRound = recentRounds.OrderByDescending(r => r.RoundEndTime).First();
                        
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
                            Metadata = $"{{\"positive_percentage\":{positivePercentage:F1},\"positive_rounds\":{positiveKdRounds},\"total_rounds\":{recentRounds.Count}}}"
                        });

                        _logger.LogInformation("Consistency badge achieved: {PlayerName} had positive KD in {Percentage:F1}% of last {Rounds} rounds",
                            playerName, positivePercentage, recentRounds.Count);
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
} 