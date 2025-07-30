using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class GamificationService
{
    private readonly ClickHouseGamificationService _clickHouseService;
    private readonly KillStreakDetector _killStreakDetector;
    private readonly MilestoneCalculator _milestoneCalculator;
    private readonly PerformanceBadgeCalculator _performanceBadgeCalculator;
    private readonly BadgeDefinitionsService _badgeDefinitionsService;
    private readonly HistoricalProcessor _historicalProcessor;
    private readonly ILogger<GamificationService> _logger;

    public GamificationService(
        ClickHouseGamificationService clickHouseService,
        KillStreakDetector killStreakDetector,
        MilestoneCalculator milestoneCalculator,
        PerformanceBadgeCalculator performanceBadgeCalculator,
        BadgeDefinitionsService badgeDefinitionsService,
        HistoricalProcessor historicalProcessor,
        ILogger<GamificationService> logger)
    {
        _clickHouseService = clickHouseService;
        _killStreakDetector = killStreakDetector;
        _milestoneCalculator = milestoneCalculator;
        _performanceBadgeCalculator = performanceBadgeCalculator;
        _badgeDefinitionsService = badgeDefinitionsService;
        _historicalProcessor = historicalProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Main incremental processing method - processes only new rounds since last run
    /// </summary>
    public async Task ProcessNewAchievementsAsync()
    {
        try
        {
            // Get the last time we processed achievements
            var lastProcessed = await _clickHouseService.GetLastProcessedTimestampAsync();
            var now = DateTime.UtcNow;
            
            _logger.LogInformation("Processing achievements since {LastProcessed}", lastProcessed);

            // Only process new player_rounds since last run
            var newRounds = await _clickHouseService.GetPlayerRoundsSinceAsync(lastProcessed);
            
            if (!newRounds.Any()) 
            {
                _logger.LogInformation("No new rounds to process");
                return;
            }

            _logger.LogInformation("Processing {RoundCount} new rounds for gamification", newRounds.Count);

            // Calculate all achievements for these new rounds
            var allAchievements = await ProcessAchievementsForRounds(newRounds);
            
            // Store achievements in batch for efficiency
            if (allAchievements.Any())
            {
                await _clickHouseService.InsertAchievementsBatchAsync(allAchievements);
                _logger.LogInformation("Stored {AchievementCount} new achievements", allAchievements.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new achievements");
            throw;
        }
    }

    /// <summary>
    /// Process achievements for a specific set of rounds
    /// </summary>
    public async Task<List<Achievement>> ProcessAchievementsForRounds(List<PlayerRound> rounds)
    {
        var allAchievements = new List<Achievement>();

        try
        {
            foreach (var round in rounds)
            {
                var roundAchievements = new List<Achievement>();

                // 1. Kill Streak Achievements (single-round only)
                var streakAchievements = await _killStreakDetector.CalculateKillStreaksForRoundAsync(round);
                roundAchievements.AddRange(streakAchievements);

                // 2. Milestone Achievements (check if any thresholds crossed)
                var milestoneAchievements = await _milestoneCalculator.CheckMilestoneCrossedAsync(round);
                roundAchievements.AddRange(milestoneAchievements);

                // 3. Performance Badge Checks (triggered by new round data)
                var performanceAchievements = await _performanceBadgeCalculator.CheckPerformanceBadgesAsync(round);
                roundAchievements.AddRange(performanceAchievements);

                allAchievements.AddRange(roundAchievements);

                if (roundAchievements.Any())
                {
                    _logger.LogDebug("Round {RoundId} for {PlayerName}: {AchievementCount} achievements",
                        round.RoundId, round.PlayerName, roundAchievements.Count);
                }
            }

            _logger.LogInformation("Processed {RoundCount} rounds, generated {AchievementCount} achievements",
                rounds.Count, allAchievements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing achievements for rounds");
            throw;
        }

        return allAchievements;
    }

    /// <summary>
    /// Get comprehensive achievement summary for a player
    /// </summary>
    public async Task<PlayerAchievementSummary> GetPlayerAchievementSummaryAsync(string playerName)
    {
        try
        {
            // Get all achievements for the player
            var allAchievements = await _clickHouseService.GetPlayerAchievementsAsync(playerName, 1000);
            
            // Get kill streak stats
            var streakStats = await _killStreakDetector.GetPlayerKillStreakStatsAsync(playerName);

            // Categorize achievements
            var recentAchievements = allAchievements
                .Where(a => a.AchievedAt >= DateTime.UtcNow.AddDays(-30))
                .OrderByDescending(a => a.AchievedAt)
                .Take(20)
                .ToList();

            var badges = allAchievements
                .Where(a => a.AchievementType == AchievementTypes.Badge)
                .OrderByDescending(a => a.AchievedAt)
                .ToList();

            var milestones = allAchievements
                .Where(a => a.AchievementType == AchievementTypes.Milestone)
                .OrderByDescending(a => a.Value)
                .ToList();

            return new PlayerAchievementSummary
            {
                PlayerName = playerName,
                RecentAchievements = recentAchievements,
                AllBadges = badges,
                Milestones = milestones,
                BestStreaks = streakStats,
                LastCalculated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting achievement summary for player {PlayerName}", playerName);
            throw;
        }
    }

    /// <summary>
    /// Get leaderboard for specific achievement type
    /// </summary>
    public async Task<GamificationLeaderboard> GetLeaderboardAsync(string category, string period = "all_time", int limit = 100)
    {
        try
        {
            var entries = category.ToLower() switch
            {
                "kill_streaks" => await _clickHouseService.GetKillStreakLeaderboardAsync(limit),
                _ => new List<LeaderboardEntry>()
            };

            return new GamificationLeaderboard
            {
                Category = category,
                Period = period,
                Entries = entries,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting leaderboard for category {Category}", category);
            throw;
        }
    }

    /// <summary>
    /// Historical processing for initial migration - uses ClickHouse-native approach
    /// </summary>
    public async Task ProcessHistoricalDataAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var startDate = fromDate ?? DateTime.UtcNow.AddMonths(-6); // Default: 6 months
            var endDate = toDate ?? DateTime.UtcNow;

            _logger.LogInformation("Starting historical gamification processing from {StartDate} to {EndDate}",
                startDate, endDate);

            // Use the injected historical processor that leverages ClickHouse native operations
            // This reduces query count from ~100k individual queries to ~10-50 aggregate queries
            await _historicalProcessor.ProcessHistoricalDataAsync(startDate, endDate);

            _logger.LogInformation("Historical gamification processing completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during historical processing");
            throw;
        }
    }

    /// <summary>
    /// Legacy historical processing method - kept for reference but not recommended for large datasets
    /// </summary>
    [Obsolete("Use ProcessHistoricalDataAsync() which now uses optimized processing. This method is inefficient for large datasets.")]
    public async Task ProcessHistoricalDataLegacyAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var startDate = fromDate ?? DateTime.UtcNow.AddDays(-1);
            var endDate = toDate ?? DateTime.UtcNow;

            _logger.LogWarning("Using LEGACY historical processing - this is inefficient for large datasets");
            _logger.LogInformation("Starting legacy historical gamification processing from {StartDate} to {EndDate}",
                startDate, endDate);

            // Process in monthly chunks to avoid memory issues
            var currentMonth = new DateTime(startDate.Year, startDate.Month, 1);
            
            while (currentMonth < endDate)
            {
                var monthEnd = currentMonth.AddMonths(1).AddDays(-1);
                if (monthEnd > endDate) monthEnd = endDate;
                
                _logger.LogInformation("Processing historical month: {Month:yyyy-MM}", currentMonth);
                
                var monthRounds = await _clickHouseService.GetPlayerRoundsInPeriodAsync(currentMonth, monthEnd);
                
                if (monthRounds.Any())
                {
                    var monthAchievements = await ProcessAchievementsForRounds(monthRounds);
                    
                    if (monthAchievements.Any())
                    {
                        await _clickHouseService.InsertAchievementsBatchAsync(monthAchievements);
                        _logger.LogInformation("Processed {RoundCount} rounds, {AchievementCount} achievements for {Month:yyyy-MM}",
                            monthRounds.Count, monthAchievements.Count, currentMonth);
                    }
                }
                
                currentMonth = currentMonth.AddMonths(1);
                
                // Small delay to avoid overwhelming ClickHouse
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            _logger.LogInformation("Legacy historical gamification processing completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during legacy historical processing");
            throw;
        }
    }

    /// <summary>
    /// Get available badge definitions
    /// </summary>
    public List<BadgeDefinition> GetAllBadgeDefinitions()
    {
        return _badgeDefinitionsService.GetAllBadges();
    }

    /// <summary>
    /// Get badge definitions by category
    /// </summary>
    public List<BadgeDefinition> GetBadgeDefinitionsByCategory(string category)
    {
        return _badgeDefinitionsService.GetBadgesByCategory(category);
    }

    /// <summary>
    /// Check if a player has a specific achievement
    /// </summary>
    public async Task<bool> PlayerHasAchievementAsync(string playerName, string achievementId)
    {
        return await _clickHouseService.PlayerHasAchievementAsync(playerName, achievementId);
    }
} 