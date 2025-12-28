using api.Gamification.Models;
using api.ClickHouse.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Gamification.Services;

public class MilestoneCalculator(
    SqliteGamificationService readService,
    BadgeDefinitionsService badgeService,
    PlayerTrackerDbContext dbContext,
    ILogger<MilestoneCalculator> logger)
{

    // Milestone thresholds
    private readonly int[] _killMilestones = [100, 500, 1000, 2500, 5000, 10000, 25000, 50000];
    private readonly int[] _playtimeHourMilestones = [10, 50, 100, 500, 1000];
    private readonly int[] _scoreMilestones = [10000, 50000, 100000, 500000, 1000000];

    public async Task<List<Achievement>> CheckMilestoneCrossedAsync(PlayerRound round)
    {
        var candidateAchievements = new List<Achievement>();

        try
        {
            // Pull only the milestone IDs (not full records) the player already owns - much more memory efficient
            var existingMilestoneIds = await readService.GetPlayerAchievementIdsByTypeAsync(
                round.PlayerName, AchievementTypes.Milestone);

            // Get player's totals before this round from SQLite
            var previousStats = await GetPlayerStatsBeforeTimestampAsync(
                round.PlayerName, round.RoundEndTime) ?? new PlayerGameStats { PlayerName = round.PlayerName };

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

            // Collect candidate milestones
            candidateAchievements.AddRange(await CheckKillMilestones(previousStats, newStats, round));
            candidateAchievements.AddRange(await CheckPlaytimeMilestones(previousStats, newStats, round));
            candidateAchievements.AddRange(await CheckScoreMilestones(previousStats, newStats, round));

            // 1. Remove duplicates generated within this processing batch
            var distinctById = candidateAchievements
                .GroupBy(a => a.AchievementId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // 2. Filter out milestones the player already possesses in ClickHouse
            var newUniqueAchievements = distinctById
                .Where(a => !existingMilestoneIds.Contains(a.AchievementId))
                .ToList();

            return newUniqueAchievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking milestones for player {PlayerName}, round {RoundId}",
                round.PlayerName, round.RoundId);
            return new List<Achievement>();
        }
    }

    private Task<List<Achievement>> CheckKillMilestones(
        PlayerGameStats previousStats, PlayerGameStats newStats, PlayerRound round)
    {
        var achievements = new List<Achievement>();

        foreach (var milestone in _killMilestones)
        {
            if (previousStats.TotalKills < milestone && newStats.TotalKills >= milestone)
            {
                // Player crossed this kill milestone
                var badgeDefinition = badgeService.GetBadgeDefinition($"total_kills_{milestone}");
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
                        Metadata = $"{{\"previous_kills\":{previousStats.TotalKills},\"new_kills\":{newStats.TotalKills}}}",
                        Game = round.Game ?? "unknown",
                        Version = round.RoundEndTime  // Use round end time as deterministic version for idempotency
                    });

                    logger.LogInformation("Kill milestone achieved: {PlayerName} reached {Milestone} total kills",
                        round.PlayerName, milestone);
                }
            }
        }

        return Task.FromResult(achievements);
    }

    private Task<List<Achievement>> CheckPlaytimeMilestones(
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
                var badgeDefinition = badgeService.GetBadgeDefinition($"milestone_playtime_{milestoneHours}h");
                if (badgeDefinition != null)
                {
                    achievements.Add(new Achievement
                    {
                        PlayerName = round.PlayerName,
                        AchievementType = AchievementTypes.Milestone,
                        AchievementId = $"milestone_playtime_{milestoneHours}h",
                        AchievementName = badgeDefinition.Name,
                        Tier = badgeDefinition.Tier,
                        Value = (uint)milestoneHours,
                        AchievedAt = round.RoundEndTime,
                        ProcessedAt = DateTime.UtcNow,
                        ServerGuid = round.ServerGuid,
                        MapName = round.MapName,
                        RoundId = round.RoundId,
                        Metadata = $"{{\"previous_hours\":{previousStats.TotalPlayTimeMinutes / 60.0:F1},\"new_hours\":{newStats.TotalPlayTimeMinutes / 60.0:F1}}}",
                        Game = round.Game ?? "unknown",
                        Version = round.RoundEndTime  // Use round end time as deterministic version for idempotency
                    });

                    logger.LogInformation("Playtime milestone achieved: {PlayerName} reached {Milestone} hours played",
                        round.PlayerName, milestoneHours);
                }
            }
        }

        return Task.FromResult(achievements);
    }

    private Task<List<Achievement>> CheckScoreMilestones(
        PlayerGameStats previousStats, PlayerGameStats newStats, PlayerRound round)
    {
        var achievements = new List<Achievement>();

        foreach (var milestone in _scoreMilestones)
        {
            if (previousStats.TotalScore < milestone && newStats.TotalScore >= milestone)
            {
                // Player crossed this score milestone
                var badgeDefinition = badgeService.GetBadgeDefinition($"total_score_{milestone}");
                if (badgeDefinition != null)
                {
                    achievements.Add(new Achievement
                    {
                        PlayerName = round.PlayerName,
                        AchievementType = AchievementTypes.Milestone,
                        AchievementId = $"total_score_{milestone}",
                        AchievementName = badgeDefinition.Name,
                        Tier = badgeDefinition.Tier,
                        Value = (uint)milestone,
                        AchievedAt = round.RoundEndTime,
                        ProcessedAt = DateTime.UtcNow,
                        ServerGuid = round.ServerGuid,
                        MapName = round.MapName,
                        RoundId = round.RoundId,
                        Metadata = $"{{\"previous_score\":{previousStats.TotalScore},\"new_score\":{newStats.TotalScore}}}",
                        Game = round.Game ?? "unknown",
                        Version = round.RoundEndTime  // Use round end time as deterministic version for idempotency
                    });

                    logger.LogInformation("Score milestone achieved: {PlayerName} reached {Milestone:N0} total score",
                        round.PlayerName, milestone);
                }
            }
        }

        return Task.FromResult(achievements);
    }



    public async Task<List<Achievement>> GetPlayerMilestonesAsync(string playerName)
    {
        try
        {
            return await readService.GetPlayerAchievementsByTypeAsync(playerName, AchievementTypes.Milestone);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting milestones for player {PlayerName}", playerName);
            return new List<Achievement>();
        }
    }

    public List<BadgeDefinition> GetAvailableMilestones()
    {
        return badgeService.GetBadgesByCategory(BadgeCategories.Milestone);
    }

    /// <summary>
    /// Get player's cumulative stats before a specific timestamp using PlayerStatsMonthly aggregates.
    /// </summary>
    private async Task<PlayerGameStats?> GetPlayerStatsBeforeTimestampAsync(string playerName, DateTime beforeTimestamp)
    {
        try
        {
            var beforeInstant = NodaTime.Instant.FromDateTimeUtc(DateTime.SpecifyKind(beforeTimestamp, DateTimeKind.Utc));

            // Use pre-aggregated monthly stats - much more efficient than scanning sessions
            var monthlyStats = await dbContext.PlayerStatsMonthly
                .Where(ps => ps.PlayerName == playerName && ps.LastRoundTime < beforeInstant)
                .ToListAsync();

            if (monthlyStats.Count == 0)
                return null;

            return new PlayerGameStats
            {
                PlayerName = playerName,
                TotalKills = monthlyStats.Sum(ps => ps.TotalKills),
                TotalDeaths = monthlyStats.Sum(ps => ps.TotalDeaths),
                TotalScore = monthlyStats.Sum(ps => ps.TotalScore),
                TotalPlayTimeMinutes = (int)monthlyStats.Sum(ps => ps.TotalPlayTimeMinutes),
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting player stats before timestamp for {PlayerName}", playerName);
            return null;
        }
    }
}
