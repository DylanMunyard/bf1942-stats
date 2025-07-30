using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class MilestoneCalculator
{
    private readonly ClickHouseGamificationService _clickHouseService;
    private readonly BadgeDefinitionsService _badgeService;
    private readonly ILogger<MilestoneCalculator> _logger;

    // Milestone thresholds
    private readonly int[] _killMilestones = { 100, 500, 1000, 2500, 5000, 10000, 25000, 50000 };
    private readonly int[] _playtimeHourMilestones = { 10, 50, 100, 500, 1000 };
    private readonly int[] _scoreMilestones = { 10000, 50000, 100000, 500000, 1000000 };

    public MilestoneCalculator(
        ClickHouseGamificationService clickHouseService,
        BadgeDefinitionsService badgeService,
        ILogger<MilestoneCalculator> logger)
    {
        _clickHouseService = clickHouseService;
        _badgeService = badgeService;
        _logger = logger;
    }

    public async Task<List<Achievement>> CheckMilestoneCrossedAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Get player's totals before this round
            var previousStats = await _clickHouseService.GetPlayerStatsBeforeTimestampAsync(
                round.PlayerName, round.RoundEndTime);

            if (previousStats == null)
            {
                previousStats = new PlayerGameStats { PlayerName = round.PlayerName };
            }

            // Calculate new totals after this round
            var newStats = new PlayerGameStats
            {
                PlayerName = round.PlayerName,
                TotalKills = previousStats.TotalKills + round.Kills,
                TotalDeaths = previousStats.TotalDeaths + round.Deaths,
                TotalScore = previousStats.TotalScore + round.Score,
                TotalPlayTimeMinutes = previousStats.TotalPlayTimeMinutes + (int)round.PlayTimeMinutes,
                LastUpdated = DateTime.UtcNow
            };

            // Check each milestone type
            achievements.AddRange(await CheckKillMilestones(previousStats, newStats, round));
            achievements.AddRange(await CheckPlaytimeMilestones(previousStats, newStats, round));
            achievements.AddRange(await CheckScoreMilestones(previousStats, newStats, round));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking milestones for player {PlayerName}, round {RoundId}",
                round.PlayerName, round.RoundId);
        }

        return achievements;
    }

    private async Task<List<Achievement>> CheckKillMilestones(
        PlayerGameStats previousStats, PlayerGameStats newStats, PlayerRound round)
    {
        var achievements = new List<Achievement>();

        foreach (var milestone in _killMilestones)
        {
            if (previousStats.TotalKills < milestone && newStats.TotalKills >= milestone)
            {
                // Player crossed this kill milestone
                var badgeDefinition = _badgeService.GetBadgeDefinition($"total_kills_{milestone}");
                if (badgeDefinition != null)
                {
                    achievements.Add(new Achievement
                    {
                        PlayerName = round.PlayerName,
                        AchievementType = AchievementTypes.Milestone,
                        AchievementId = $"total_kills_{milestone}",
                        AchievementName = badgeDefinition.Name,
                        Tier = badgeDefinition.Tier,
                        Value = (uint)milestone,
                        AchievedAt = round.RoundEndTime,
                        ProcessedAt = DateTime.UtcNow,
                        ServerGuid = round.ServerGuid,
                        MapName = round.MapName,
                        RoundId = round.RoundId,
                        Metadata = $"{{\"previous_kills\":{previousStats.TotalKills},\"new_kills\":{newStats.TotalKills}}}"
                    });

                    _logger.LogInformation("Kill milestone achieved: {PlayerName} reached {Milestone} total kills",
                        round.PlayerName, milestone);
                }
            }
        }

        return achievements;
    }

    private async Task<List<Achievement>> CheckPlaytimeMilestones(
        PlayerGameStats previousStats, PlayerGameStats newStats, PlayerRound round)
    {
        var achievements = new List<Achievement>();

        foreach (var milestoneHours in _playtimeHourMilestones)
        {
            var milestoneMinutes = milestoneHours * 60;
            
            if (previousStats.TotalPlayTimeMinutes < milestoneMinutes && 
                newStats.TotalPlayTimeMinutes >= milestoneMinutes)
            {
                // Player crossed this playtime milestone
                var badgeDefinition = _badgeService.GetBadgeDefinition($"playtime_{milestoneHours}h");
                if (badgeDefinition != null)
                {
                    achievements.Add(new Achievement
                    {
                        PlayerName = round.PlayerName,
                        AchievementType = AchievementTypes.Milestone,
                        AchievementId = $"playtime_{milestoneHours}h",
                        AchievementName = badgeDefinition.Name,
                        Tier = badgeDefinition.Tier,
                        Value = (uint)milestoneHours,
                        AchievedAt = round.RoundEndTime,
                        ProcessedAt = DateTime.UtcNow,
                        ServerGuid = round.ServerGuid,
                        MapName = round.MapName,
                        RoundId = round.RoundId,
                        Metadata = $"{{\"previous_hours\":{previousStats.TotalPlayTimeMinutes / 60.0:F1},\"new_hours\":{newStats.TotalPlayTimeMinutes / 60.0:F1}}}"
                    });

                    _logger.LogInformation("Playtime milestone achieved: {PlayerName} reached {Milestone} hours played",
                        round.PlayerName, milestoneHours);
                }
            }
        }

        return achievements;
    }

    private async Task<List<Achievement>> CheckScoreMilestones(
        PlayerGameStats previousStats, PlayerGameStats newStats, PlayerRound round)
    {
        var achievements = new List<Achievement>();

        foreach (var milestone in _scoreMilestones)
        {
            if (previousStats.TotalScore < milestone && newStats.TotalScore >= milestone)
            {
                // Player crossed this score milestone
                achievements.Add(new Achievement
                {
                    PlayerName = round.PlayerName,
                    AchievementType = AchievementTypes.Milestone,
                    AchievementId = $"total_score_{milestone}",
                    AchievementName = $"{milestone:N0} Total Score",
                    Tier = GetScoreMilestoneTier(milestone),
                    Value = (uint)milestone,
                    AchievedAt = round.RoundEndTime,
                    ProcessedAt = DateTime.UtcNow,
                    ServerGuid = round.ServerGuid,
                    MapName = round.MapName,
                    RoundId = round.RoundId,
                    Metadata = $"{{\"previous_score\":{previousStats.TotalScore},\"new_score\":{newStats.TotalScore}}}"
                });

                _logger.LogInformation("Score milestone achieved: {PlayerName} reached {Milestone:N0} total score",
                    round.PlayerName, milestone);
            }
        }

        return achievements;
    }

    private string GetScoreMilestoneTier(int scoreThreshold)
    {
        return scoreThreshold switch
        {
            <= 50000 => BadgeTiers.Bronze,
            <= 100000 => BadgeTiers.Silver,
            <= 500000 => BadgeTiers.Gold,
            _ => BadgeTiers.Legend
        };
    }

    public async Task<List<Achievement>> GetPlayerMilestonesAsync(string playerName)
    {
        try
        {
            return await _clickHouseService.GetPlayerAchievementsByTypeAsync(playerName, AchievementTypes.Milestone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting milestones for player {PlayerName}", playerName);
            return new List<Achievement>();
        }
    }

    public List<BadgeDefinition> GetAvailableMilestones()
    {
        return _badgeService.GetBadgesByCategory(BadgeCategories.Milestone);
    }
} 