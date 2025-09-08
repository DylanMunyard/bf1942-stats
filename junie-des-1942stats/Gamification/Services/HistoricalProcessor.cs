using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.ClickHouse.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace junie_des_1942stats.Gamification.Services;

/// <summary>
/// Historical processor that uses ClickHouse native operations
/// instead of round-by-round processing to dramatically reduce query count
/// </summary>
public class HistoricalProcessor
{
    private readonly ClickHouseGamificationService _gamificationService;
    private readonly BadgeDefinitionsService _badgeDefinitionsService;
    private readonly IClickHouseReader _clickHouseReader;
    private readonly ILogger<HistoricalProcessor> _logger;

    // Milestone thresholds
    private readonly int[] _killMilestones = { 100, 500, 1000, 2500, 5000, 10000, 25000, 50000 };
    private readonly int[] _playtimeHourMilestones = { 10, 50, 100, 500, 1000 };
    private readonly int[] _scoreMilestones = { 10000, 50000, 100000, 500000, 1000000 };

    public HistoricalProcessor(
        ClickHouseGamificationService gamificationService,
        BadgeDefinitionsService badgeDefinitionsService,
        IClickHouseReader clickHouseReader,
        ILogger<HistoricalProcessor> logger)
    {
        _gamificationService = gamificationService;
        _badgeDefinitionsService = badgeDefinitionsService;
        _clickHouseReader = clickHouseReader;
        _logger = logger;
    }

    /// <summary>
    /// Process historical data efficiently using ClickHouse native operations
    /// </summary>
    public async Task ProcessHistoricalDataAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var startDate = fromDate ?? DateTime.UtcNow.AddMonths(-6);
        // Ensure endDate includes the full day to avoid boundary issues
        var endDate = toDate ?? DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1);

        _logger.LogInformation("Starting historical processing from {StartDate} to {EndDate}",
            startDate, endDate);

        _logger.LogInformation("Date calculation details - Current time: {CurrentTime}, Start: {StartDate}, End: {EndDate}",
            DateTime.UtcNow, startDate, endDate);

        try
        {
            // Process milestones first (fastest - pure aggregation)
            await ProcessMilestonesAsync(startDate, endDate);

            // Process kill streaks with boundary handling
            await ProcessKillStreaksAsync(startDate, endDate);

            // Process performance badges (if needed)
            await ProcessPerformanceBadgesAsync(startDate, endDate);

            _logger.LogInformation("Historical processing completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during historical processing");
            throw;
        }
    }

    /// <summary>
    /// Process all milestones with accurate achievement dates using running totals
    /// </summary>
    private async Task ProcessMilestonesAsync(DateTime startDate, DateTime endDate)
    {
        _logger.LogInformation("Processing milestones with accurate achievement dates...");

        var achievements = new List<Achievement>();

        // Get existing achievements to avoid duplicates
        var existingAchievements = await GetExistingMilestoneAchievementsAsync();

        // Process each milestone type separately for better performance
        achievements.AddRange(await ProcessKillMilestonesAsync(startDate, endDate, existingAchievements));
        achievements.AddRange(await ProcessScoreMilestonesAsync(startDate, endDate, existingAchievements));
        achievements.AddRange(await ProcessPlaytimeMilestonesAsync(startDate, endDate, existingAchievements));

        if (achievements.Any())
        {
            await _gamificationService.InsertAchievementsBatchAsync(achievements);
            _logger.LogInformation("Created {AchievementCount} milestone achievements with accurate dates",
                achievements.Count);
        }
        else
        {
            _logger.LogInformation("No new milestone achievements to create");
        }
    }

    /// <summary>
    /// Process kill streaks using ClickHouse window functions with boundary handling
    /// </summary>
    private async Task ProcessKillStreaksAsync(DateTime startDate, DateTime endDate)
    {
        _logger.LogInformation("Processing kill streaks with ClickHouse window functions...");

        // Get existing kill streak achievements to avoid duplicates
        var existingKillStreakAchievements = await GetExistingKillStreakAchievementsAsync();

        // Process in monthly chunks with overlap to handle boundary-crossing streaks
        var currentMonth = new DateTime(startDate.Year, startDate.Month, 1);

        // Use <= to ensure we process the month containing endDate
        while (currentMonth <= endDate)
        {
            var monthEnd = currentMonth.AddMonths(1).AddDays(-1);
            if (monthEnd > endDate) monthEnd = endDate;

            // Add overlap - look back 1 day to catch streaks that cross boundaries
            var overlapStart = currentMonth.AddDays(-1);

            _logger.LogInformation("Processing streaks for period {Start} to {End} (with overlap from {OverlapStart})",
                currentMonth, monthEnd, overlapStart);

            var streakAchievements = await DetectKillStreaksInPeriodAsync(overlapStart, monthEnd, existingKillStreakAchievements);

            // Filter out achievements that are too old (from overlap period)
            var newAchievements = streakAchievements
                .Where(a => a.AchievedAt >= currentMonth)
                .ToList();

            if (newAchievements.Any())
            {
                await _gamificationService.InsertAchievementsBatchAsync(newAchievements);
                _logger.LogInformation("Found {AchievementCount} new kill streak achievements for {Month:yyyy-MM}",
                    newAchievements.Count, currentMonth);
            }
            else
            {
                _logger.LogInformation("No new kill streak achievements found for {Month:yyyy-MM}", currentMonth);
            }

            currentMonth = currentMonth.AddMonths(1);

            // Small delay to avoid overwhelming ClickHouse
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Detect kill streaks using ClickHouse window functions on player_metrics table
    /// </summary>
    private async Task<List<Achievement>> DetectKillStreaksInPeriodAsync(DateTime startDate, DateTime endDate, Dictionary<string, HashSet<string>> existingAchievements)
    {
        _logger.LogInformation("Detecting kill streaks from player_metrics snapshots...");

        // Use ClickHouse window functions to detect kill streaks within single rounds
        // First identify round boundaries, then calculate streaks within each round
        var query = $@"
WITH player_deltas AS (
    SELECT 
        timestamp,
        server_guid,
        server_name,
        player_name,
        map_name,
        kills,
        deaths,
        kills - lagInFrame(kills, 1, kills) OVER w AS kill_delta,
        deaths - lagInFrame(deaths, 1, deaths) OVER w AS death_delta,
        timestamp - lagInFrame(timestamp, 1, timestamp) OVER w AS time_delta
    FROM player_metrics 
    WHERE timestamp >= '{startDate:yyyy-MM-dd HH:mm:ss}'
    AND timestamp <= '{endDate:yyyy-MM-dd HH:mm:ss}'
    AND (kills > 0 OR deaths > 0)  -- Only consider rows with activity
    WINDOW w AS (PARTITION BY server_guid, player_name, map_name ORDER BY timestamp)
),
round_boundaries AS (
    SELECT *,
        -- Create round boundaries: new round when >5min gap or map change
        CASE WHEN time_delta > 300 OR map_name != lagInFrame(map_name, 1, map_name) OVER w2 THEN 1 ELSE 0 END AS round_start
    FROM player_deltas
    WHERE kill_delta >= 0 AND death_delta >= 0  -- Filter out counter resets
    WINDOW w2 AS (PARTITION BY server_guid, player_name ORDER BY timestamp)
),
round_groups AS (
    SELECT *,
        -- Assign round IDs by cumulative sum of round starts
        sum(round_start) OVER (PARTITION BY server_guid, player_name ORDER BY timestamp) AS round_id
    FROM round_boundaries
),
streak_analysis AS (
    SELECT *,
        -- Reset streak when player dies OR round changes
        CASE WHEN death_delta > 0 THEN 1 ELSE 0 END AS streak_reset,
        -- Only count positive kill deltas (new kills) when no deaths
        CASE WHEN kill_delta > 0 AND death_delta = 0 THEN kill_delta ELSE 0 END AS streak_kills
    FROM round_groups
),
streak_groups AS (
    SELECT *,
        -- Create streak group IDs within each round
        sum(streak_reset) OVER (PARTITION BY server_guid, player_name, round_id ORDER BY timestamp) AS streak_group_id
    FROM streak_analysis
),
max_streaks AS (
    SELECT 
        server_guid,
        server_name,
        player_name,
        map_name,
        round_id,
        streak_group_id,
        sum(streak_kills) AS total_streak_kills,
        min(timestamp) AS streak_start,
        max(timestamp) AS streak_end,
        count(*) AS streak_observations
    FROM streak_groups
    WHERE streak_kills > 0  -- Only count observations with kills
    GROUP BY server_guid, server_name, player_name, map_name, round_id, streak_group_id
    HAVING total_streak_kills >= 5  -- Minimum streak threshold
)
SELECT 
    server_guid,
    server_name, 
    player_name,
    map_name,
    total_streak_kills AS max_streak,
    streak_start,
    streak_end
FROM max_streaks
WHERE total_streak_kills >= 5
ORDER BY total_streak_kills DESC, player_name, streak_start";

        try
        {
            var result = await QueryPlayerMetricsAsync(query);
            var streakData = ParseStreakData(result);

            _logger.LogInformation("Found {StreakCount} potential kill streaks", streakData.Count);

            var achievements = new List<Achievement>();

            foreach (var streak in streakData)
            {
                var streakAchievements = CreateStreakAchievementsForDetectedStreak(streak, existingAchievements);
                achievements.AddRange(streakAchievements);
            }

            return achievements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting kill streaks in period {StartDate} to {EndDate}", startDate, endDate);
            return new List<Achievement>();
        }
    }

    /// <summary>
    /// Process performance badges using aggregated data
    /// </summary>
    private Task ProcessPerformanceBadgesAsync(DateTime startDate, DateTime endDate)
    {
        _logger.LogInformation("Processing performance badges...");

        // This could include badges like:
        // - "Consistent Performer" (good K/D ratio over multiple rounds)
        // - "Map Master" (played X rounds on specific maps)
        // - "Server Regular" (played Y hours on specific servers)

        // Implementation would use similar aggregation patterns as milestones
        // For now, logging placeholder

        _logger.LogInformation("Performance badge processing completed (placeholder)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Process kill milestones with accurate achievement dates
    /// </summary>
    private async Task<List<Achievement>> ProcessKillMilestonesAsync(DateTime startDate, DateTime endDate, Dictionary<string, HashSet<string>> existingAchievements)
    {
        var thresholds = string.Join(",", _killMilestones);
        var query = $@"
WITH running_totals AS (
    SELECT 
        player_name,
        round_end_time,
        final_kills,
        SUM(final_kills) OVER (
            PARTITION BY player_name 
            ORDER BY round_end_time ASC 
            ROWS UNBOUNDED PRECEDING
        ) AS running_kills
    FROM player_rounds
    WHERE round_end_time >= '{startDate:yyyy-MM-dd HH:mm:ss}'
    AND round_end_time <= '{endDate:yyyy-MM-dd HH:mm:ss}'
    AND is_bot = 0
    ORDER BY player_name, round_end_time
),
milestone_crossings AS (
    SELECT 
        player_name,
        round_end_time,
        running_kills,
        arrayElement([{thresholds}], number) AS threshold
    FROM running_totals
    ARRAY JOIN range(1, length([{thresholds}]) + 1) AS number
    WHERE running_kills >= arrayElement([{thresholds}], number)
    AND (running_kills - final_kills) < arrayElement([{thresholds}], number)
)
SELECT 
    player_name,
    threshold,
    min(round_end_time) AS achievement_date
FROM milestone_crossings
GROUP BY player_name, threshold
ORDER BY player_name, threshold";

        var result = await QueryPlayerRoundsAsync(query);
        return ParseMilestoneAchievements(result, "kills", existingAchievements);
    }

    /// <summary>
    /// Process score milestones with accurate achievement dates
    /// </summary>
    private async Task<List<Achievement>> ProcessScoreMilestonesAsync(DateTime startDate, DateTime endDate, Dictionary<string, HashSet<string>> existingAchievements)
    {
        var thresholds = string.Join(",", _scoreMilestones);
        var query = $@"
WITH running_totals AS (
    SELECT 
        player_name,
        round_end_time,
        final_score,
        SUM(final_score) OVER (
            PARTITION BY player_name 
            ORDER BY round_end_time ASC 
            ROWS UNBOUNDED PRECEDING
        ) AS running_score
    FROM player_rounds
    WHERE round_end_time >= '{startDate:yyyy-MM-dd HH:mm:ss}'
    AND round_end_time <= '{endDate:yyyy-MM-dd HH:mm:ss}'
    ORDER BY player_name, round_end_time
),
milestone_crossings AS (
    SELECT 
        player_name,
        round_end_time,
        running_score,
        arrayElement([{thresholds}], number) AS threshold
    FROM running_totals
    ARRAY JOIN range(1, length([{thresholds}]) + 1) AS number
    WHERE running_score >= arrayElement([{thresholds}], number)
    AND (running_score - final_score) < arrayElement([{thresholds}], number)
)
SELECT 
    player_name,
    threshold,
    min(round_end_time) AS achievement_date
FROM milestone_crossings
GROUP BY player_name, threshold
ORDER BY player_name, threshold";

        var result = await QueryPlayerRoundsAsync(query);
        return ParseMilestoneAchievements(result, "score", existingAchievements);
    }

    /// <summary>
    /// Process playtime milestones with accurate achievement dates
    /// </summary>
    private async Task<List<Achievement>> ProcessPlaytimeMilestonesAsync(DateTime startDate, DateTime endDate, Dictionary<string, HashSet<string>> existingAchievements)
    {
        var thresholds = string.Join(",", _playtimeHourMilestones.Select(h => h * 60)); // Convert hours to minutes
        var query = $@"
WITH running_totals AS (
    SELECT 
        player_name,
        round_end_time,
        play_time_minutes,
        SUM(play_time_minutes) OVER (
            PARTITION BY player_name 
            ORDER BY round_end_time ASC 
            ROWS UNBOUNDED PRECEDING
        ) AS running_playtime
    FROM player_rounds
    WHERE round_end_time >= '{startDate:yyyy-MM-dd HH:mm:ss}'
    AND round_end_time <= '{endDate:yyyy-MM-dd HH:mm:ss}'
    ORDER BY player_name, round_end_time
),
milestone_crossings AS (
    SELECT 
        player_name,
        round_end_time,
        running_playtime,
        arrayElement([{thresholds}], number) AS threshold_minutes
    FROM running_totals
    ARRAY JOIN range(1, length([{thresholds}]) + 1) AS number
    WHERE running_playtime >= arrayElement([{thresholds}], number)
    AND (running_playtime - play_time_minutes) < arrayElement([{thresholds}], number)
)
SELECT 
    player_name,
    threshold_minutes / 60 AS threshold,
    min(round_end_time) AS achievement_date
FROM milestone_crossings
GROUP BY player_name, threshold_minutes
ORDER BY player_name, threshold";

        var result = await QueryPlayerRoundsAsync(query);
        return ParseMilestoneAchievements(result, "playtime", existingAchievements);
    }

    /// <summary>
    /// Parse milestone achievement results and create Achievement objects
    /// </summary>
    private List<Achievement> ParseMilestoneAchievements(string result, string category, Dictionary<string, HashSet<string>> existingAchievements)
    {
        var achievements = new List<Achievement>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                var playerName = parts[0];
                var threshold = int.Parse(parts[1]);
                var achievementDate = DateTime.Parse(parts[2]);

                var achievementId = category switch
                {
                    "playtime" => $"milestone_playtime_{threshold}h",
                    "kills" => $"total_kills_{threshold}",
                    "score" => $"total_score_{threshold}",
                    _ => $"milestone_{category}_{threshold}"
                };

                // Check if player already has this achievement
                if (existingAchievements.ContainsKey(playerName) &&
                    existingAchievements[playerName].Contains(achievementId))
                {
                    continue;
                }

                var name = category switch
                {
                    "kills" => $"Kill Master ({threshold:N0})",
                    "score" => $"Score Legend ({threshold:N0})",
                    "playtime" => $"Time Warrior ({threshold}h)",
                    _ => $"Milestone ({threshold:N0})"
                };

                achievements.Add(CreateMilestoneAchievement(
                    playerName, achievementId, name, threshold, achievementDate, category));
            }
        }

        return achievements;
    }

    /// <summary>
    /// Get player stats with existing achievements - optimized to avoid N+1 queries
    /// </summary>
    private async Task<List<PlayerStatsWithAchievements>> GetPlayerStatsWithExistingAchievementsAsync(DateTime startDate, DateTime endDate)
    {
        // Get player stats in single query
        var playerStats = await GetPlayerStatsInPeriodAsync(startDate, endDate);

        // Get all existing milestone achievements in single query
        var existingAchievements = await GetExistingMilestoneAchievementsAsync();

        // Combine the data
        var result = new List<PlayerStatsWithAchievements>();
        foreach (var stats in playerStats)
        {
            var playerAchievements = existingAchievements.ContainsKey(stats.PlayerName)
                ? existingAchievements[stats.PlayerName]
                : new HashSet<string>();

            result.Add(new PlayerStatsWithAchievements
            {
                Stats = stats,
                ExistingAchievementIds = playerAchievements
            });
        }

        return result;
    }

    /// <summary>
    /// Get aggregated player statistics for a time period using a single ClickHouse query
    /// </summary>
    private async Task<List<PlayerGameStats>> GetPlayerStatsInPeriodAsync(DateTime startDate, DateTime endDate)
    {
        var query = $@"
            SELECT 
                player_name,
                SUM(final_kills) as total_kills,
                SUM(final_deaths) as total_deaths,
                SUM(final_score) as total_score,
                SUM(play_time_minutes) as total_playtime,
                COUNT(*) as total_rounds
            FROM player_rounds
            WHERE round_end_time >= '{startDate:yyyy-MM-dd HH:mm:ss}'
            AND round_end_time <= '{endDate:yyyy-MM-dd HH:mm:ss}'
            AND is_bot = 0
            GROUP BY player_name
            HAVING total_rounds >= 5  -- Only process players with meaningful activity
            ORDER BY total_kills DESC";

        var result = await QueryPlayerRoundsAsync(query);
        return ParsePlayerStats(result);
    }


    private Achievement CreateMilestoneAchievement(string playerName, string achievementId,
        string name, int value, DateTime achievedAt, string category)
    {
        return new Achievement
        {
            PlayerName = playerName,
            AchievementType = AchievementTypes.Milestone,
            AchievementId = achievementId,
            AchievementName = name,
            Tier = GetMilestoneTier(value, category),
            Value = (uint)value,
            AchievedAt = achievedAt,
            ProcessedAt = DateTime.UtcNow,
            ServerGuid = "", // Not applicable for milestones
            MapName = "", // Not applicable for milestones
            RoundId = "", // Not applicable for milestones
            Metadata = $"{{\"category\":\"{category}\",\"threshold\":{value}}}",
            Game = "unknown", // Not applicable for milestones/historical data
                        Version = achievedAt  // Use achieved_at as deterministic version for idempotency
        };
    }

    private List<Achievement> CreateStreakAchievementsForDetectedStreak(StreakData streak, Dictionary<string, HashSet<string>> existingAchievements)
    {
        var achievements = new List<Achievement>();
        var thresholds = new[] { 5, 10, 15, 20, 25, 30, 50 };

        // Calculate when each threshold was achieved during this streak
        var thresholdTimes = CalculateThresholdTimesForStreak(streak);

        foreach (var threshold in thresholds)
        {
            if (thresholdTimes.TryGetValue(threshold, out var achievementTime))
            {
                var achievementId = $"kill_streak_{threshold}";

                // Check if player already has this achievement using the batch-loaded data
                var hasAchievement = existingAchievements.ContainsKey(streak.PlayerName) &&
                    existingAchievements[streak.PlayerName].Contains(achievementId);

                if (!hasAchievement)
                {
                    var badgeDefinition = _badgeDefinitionsService.GetBadgeDefinition(achievementId);
                    if (badgeDefinition != null)
                    {
                        achievements.Add(new Achievement
                        {
                            PlayerName = streak.PlayerName,
                            AchievementType = AchievementTypes.KillStreak,
                            AchievementId = achievementId,
                            AchievementName = badgeDefinition.Name,
                            Tier = badgeDefinition.Tier,
                            Value = (uint)threshold,
                            AchievedAt = achievementTime,
                            ProcessedAt = DateTime.UtcNow,
                            ServerGuid = streak.ServerGuid,
                            MapName = streak.MapName,
                            RoundId = "", // Not available from metrics data
                            Metadata = $"{{\"actual_streak\":{streak.MaxStreak},\"streak_duration_seconds\":{(streak.StreakEnd - streak.StreakStart).TotalSeconds:F0}}}",
                            Game = "unknown", // Not applicable for milestones/historical data
                        Version = achievementTime  // Use achievement time as deterministic version for idempotency
                        });
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping duplicate historical kill streak achievement: {PlayerName} already has {AchievementId}",
                        streak.PlayerName, achievementId);
                }
            }
        }

        return achievements;
    }

    /// <summary>
    /// Calculate when each threshold was achieved during a streak
    /// For historical data, we estimate threshold times based on streak duration
    /// </summary>
    private Dictionary<int, DateTime> CalculateThresholdTimesForStreak(StreakData streak)
    {
        var thresholdTimes = new Dictionary<int, DateTime>();
        var thresholds = new[] { 5, 10, 15, 20, 25, 30, 50 };

        if (streak.MaxStreak < 5) return thresholdTimes;

        var streakDuration = streak.StreakEnd - streak.StreakStart;

        // For historical data, we estimate when each threshold was achieved
        // We assume kills were distributed evenly across the streak duration
        foreach (var threshold in thresholds.Where(t => streak.MaxStreak >= t))
        {
            if (streak.MaxStreak > 0)
            {
                // Estimate the time when this threshold was reached
                // Use a linear interpolation based on the threshold position in the streak
                var progressRatio = (double)threshold / streak.MaxStreak;
                var estimatedTimeOffset = TimeSpan.FromSeconds(streakDuration.TotalSeconds * progressRatio);
                var estimatedAchievementTime = streak.StreakStart.Add(estimatedTimeOffset);

                thresholdTimes[threshold] = estimatedAchievementTime;
            }
        }

        return thresholdTimes;
    }

    private string GetMilestoneTier(int value, string category)
    {
        return category switch
        {
            "kills" => value switch
            {
                >= 50000 => "legendary",
                >= 10000 => "epic",
                >= 2500 => "rare",
                >= 500 => "uncommon",
                _ => "common"
            },
            "score" => value switch
            {
                >= 1000000 => "legendary",
                >= 500000 => "epic",
                >= 100000 => "rare",
                >= 50000 => "uncommon",
                _ => "common"
            },
            "playtime" => value switch
            {
                >= 1000 => "legendary",
                >= 500 => "epic",
                >= 100 => "rare",
                >= 50 => "uncommon",
                _ => "common"
            },
            _ => "common"
        };
    }

    private List<PlayerGameStats> ParsePlayerStats(string result)
    {
        var stats = new List<PlayerGameStats>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 6)
            {
                stats.Add(new PlayerGameStats
                {
                    PlayerName = parts[0],
                    TotalKills = int.Parse(parts[1]),
                    TotalDeaths = int.Parse(parts[2]),
                    TotalScore = int.Parse(parts[3]),
                    TotalPlayTimeMinutes = (int)Math.Round(double.Parse(parts[4])),
                    LastUpdated = DateTime.UtcNow
                });
            }
        }

        return stats;
    }

    private List<PlayerRound> ParsePlayerRounds(string result)
    {
        var rounds = new List<PlayerRound>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 8)
            {
                rounds.Add(new PlayerRound
                {
                    PlayerName = parts[0],
                    RoundId = parts[1],
                    ServerGuid = parts[2],
                    MapName = parts[3],
                    RoundEndTime = DateTime.Parse(parts[4]),
                    FinalKills = (uint)int.Parse(parts[5]),
                    FinalDeaths = (uint)int.Parse(parts[6]),
                    FinalScore = int.Parse(parts[7]),
                    PlayTimeMinutes = parts.Length > 8 ? double.Parse(parts[8]) : 0
                });
            }
        }

        return rounds;
    }

    private List<StreakData> ParseStreakData(string result)
    {
        var streaks = new List<StreakData>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 7)
            {
                streaks.Add(new StreakData
                {
                    ServerGuid = parts[0],
                    ServerName = parts[1],
                    PlayerName = parts[2],
                    MapName = parts[3],
                    MaxStreak = int.Parse(parts[4]),
                    StreakStart = DateTime.Parse(parts[5]),
                    StreakEnd = DateTime.Parse(parts[6])
                });
            }
        }

        return streaks;
    }

    /// <summary>
    /// Get all existing milestone achievements to avoid checking each one individually
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> GetExistingMilestoneAchievementsAsync()
    {
        var query = @"
            SELECT player_name, achievement_id
            FROM player_achievements_deduplicated 
            WHERE achievement_type = 'milestone'";

        var result = await QueryPlayerRoundsAsync(query);
        var existingAchievements = new Dictionary<string, HashSet<string>>();

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                var playerName = parts[0];
                var achievementId = parts[1];

                if (!existingAchievements.ContainsKey(playerName))
                {
                    existingAchievements[playerName] = new HashSet<string>();
                }
                existingAchievements[playerName].Add(achievementId);
            }
        }

        return existingAchievements;
    }

    /// <summary>
    /// Get all existing kill streak achievements to avoid duplicates
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> GetExistingKillStreakAchievementsAsync()
    {
        var query = @"
            SELECT player_name, achievement_id
            FROM player_achievements_deduplicated 
            WHERE achievement_type = 'kill_streak'";

        var result = await QueryPlayerRoundsAsync(query);
        var existingAchievements = new Dictionary<string, HashSet<string>>();

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                var playerName = parts[0];
                var achievementId = parts[1];

                if (!existingAchievements.ContainsKey(playerName))
                {
                    existingAchievements[playerName] = new HashSet<string>();
                }
                existingAchievements[playerName].Add(achievementId);
            }
        }

        return existingAchievements;
    }

    /// <summary>
    /// Query player_metrics table using ClickHouse reader service
    /// </summary>
    private async Task<string> QueryPlayerMetricsAsync(string query)
    {
        return await _clickHouseReader.ExecuteQueryAsync(query);
    }

    /// <summary>
    /// Query player_rounds table using ClickHouse reader service
    /// </summary>
    private async Task<string> QueryPlayerRoundsAsync(string query)
    {
        return await _clickHouseReader.ExecuteQueryAsync(query);
    }
}

/// <summary>
/// Represents detected kill streak data from ClickHouse analysis
/// </summary>
public class StreakData
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public int MaxStreak { get; set; }
    public DateTime StreakStart { get; set; }
    public DateTime StreakEnd { get; set; }
}

/// <summary>
/// Combines player stats with their existing achievements to avoid N+1 queries
/// </summary>
public class PlayerStatsWithAchievements
{
    public PlayerGameStats Stats { get; set; } = new();
    public HashSet<string> ExistingAchievementIds { get; set; } = new();
}