using api.Gamification.Models;
using api.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace api.Gamification.Services;

public class GamificationService(ClickHouseGamificationService gamificationService, KillStreakDetector killStreakDetector, MilestoneCalculator milestoneCalculator, PerformanceBadgeCalculator performanceBadgeCalculator, BadgeDefinitionsService badgeDefinitionsService, HistoricalProcessor historicalProcessor, AchievementLabelingService achievementLabelingService, PlacementProcessor placementProcessor, TeamVictoryProcessor teamVictoryProcessor, IConfiguration configuration, ILogger<GamificationService> logger) : IDisposable
{
    private readonly ClickHouseGamificationService _gamificationService = gamificationService;
    private readonly KillStreakDetector _killStreakDetector = killStreakDetector;
    private readonly MilestoneCalculator _milestoneCalculator = milestoneCalculator;
    private readonly PerformanceBadgeCalculator _performanceBadgeCalculator = performanceBadgeCalculator;
    private readonly BadgeDefinitionsService _badgeDefinitionsService = badgeDefinitionsService;
    private readonly HistoricalProcessor _historicalProcessor = historicalProcessor;
    private readonly AchievementLabelingService _achievementLabelingService = achievementLabelingService;
    private readonly PlacementProcessor _placementProcessor = placementProcessor;
    private readonly TeamVictoryProcessor _teamVictoryProcessor = teamVictoryProcessor;
    private readonly ILogger<GamificationService> _logger = InitializeLogger(logger, configuration);
    private readonly int _maxConcurrentRounds = configuration.GetValue<int>("GAMIFICATION_MAX_CONCURRENT_ROUNDS", 10);
    private readonly SemaphoreSlim _concurrencyThrottle = InitializeConcurrencyThrottle(configuration);

    private static ILogger<GamificationService> InitializeLogger(ILogger<GamificationService> logger, IConfiguration configuration)
    {
        var maxConcurrentRounds = configuration.GetValue<int>("GAMIFICATION_MAX_CONCURRENT_ROUNDS", 10);
        logger.LogInformation("Gamification service initialized with max concurrent rounds: {MaxConcurrentRounds}", maxConcurrentRounds);
        return logger;
    }

    private static SemaphoreSlim InitializeConcurrencyThrottle(IConfiguration configuration)
    {
        var maxConcurrentRounds = configuration.GetValue<int>("GAMIFICATION_MAX_CONCURRENT_ROUNDS", 10);
        return new SemaphoreSlim(maxConcurrentRounds, maxConcurrentRounds);
    }

    /// <summary>
    /// Main incremental processing method - processes only new rounds since last run
    /// </summary>
    public async Task ProcessNewAchievementsAsync()
    {
        try
        {
            // Get the last time we processed achievements
            var lastProcessed = await _gamificationService.GetLastProcessedTimestampAsync();
            var now = DateTime.UtcNow;

            _logger.LogInformation("Completed gamification processing cycle {LastProcessed}", lastProcessed);

            // Only process new player_rounds since last run
            var newRounds = await _gamificationService.GetPlayerRoundsSinceAsync(lastProcessed);

            List<Achievement> allAchievements = [];
            if (newRounds.Any())
            {
                _logger.LogInformation("Processing {RoundCount} new rounds for gamification", newRounds.Count);

                // Calculate all achievements for these new rounds
                allAchievements = await ProcessAchievementsForRounds(newRounds);
            }

            // Additionally, calculate placements for rounds since last placement processed
            var lastPlacementProcessed = await _gamificationService.GetLastPlacementProcessedTimestampAsync();
            var placementAchievements = await _placementProcessor.ProcessPlacementsSinceAsync(lastPlacementProcessed);
            if (placementAchievements.Any())
            {
                allAchievements.AddRange(placementAchievements);
            }

            // Process team victory achievements for rounds since last team victory processed
            var lastTeamVictoryProcessed = await _gamificationService.GetLastTeamVictoryProcessedTimestampAsync();
            var teamVictoryAchievements = await _teamVictoryProcessor.ProcessTeamVictoriesSinceAsync(lastTeamVictoryProcessed);
            if (teamVictoryAchievements.Any())
            {
                allAchievements.AddRange(teamVictoryAchievements);
            }

            // Store achievements in batch for efficiency
            if (allAchievements.Any())
            {
                await _gamificationService.InsertAchievementsBatchAsync(allAchievements);
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
    /// Process achievements for a specific set of rounds using ClickHouse player_metrics
    /// </summary>
    public async Task<List<Achievement>> ProcessAchievementsForRounds(List<PlayerRound> rounds)
    {
        try
        {
            _logger.LogInformation("Processing achievements for {RoundCount} rounds using ClickHouse player_metrics", rounds.Count);

            // Use batch processing for better performance
            if (rounds.Count > 10)
            {
                return await ProcessAchievementsForRoundsBatchAsync(rounds);
            }

            return await ProcessAchievementsIndividuallyAsync(rounds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing achievements for rounds");
            throw;
        }
    }

    /// <summary>
    /// Individual round processing for smaller batches
    /// </summary>
    private async Task<List<Achievement>> ProcessAchievementsIndividuallyAsync(List<PlayerRound> rounds)
    {
        var allAchievements = new List<Achievement>();

        foreach (var round in rounds)
        {
            var roundAchievements = new List<Achievement>();

            // 1. Kill Streak Achievements using ClickHouse player_metrics
            var streakAchievements = await _killStreakDetector.CalculateKillStreaksForRoundAsync(round);
            roundAchievements.AddRange(streakAchievements);

            // 2. Milestone Achievements 
            var milestoneAchievements = await _milestoneCalculator.CheckMilestoneCrossedAsync(round);
            roundAchievements.AddRange(milestoneAchievements);

            // 3. Performance Badge Checks using ClickHouse player_metrics
            var performanceAchievements = await _performanceBadgeCalculator.CheckPerformanceBadgesAsync(round);
            roundAchievements.AddRange(performanceAchievements);

            allAchievements.AddRange(roundAchievements);

            if (roundAchievements.Any())
            {
                _logger.LogDebug("Round {RoundId} for {PlayerName}: {AchievementCount} achievements",
                    round.RoundId, round.PlayerName, roundAchievements.Count);
            }
        }

        // Deduplicate achievements generated in this batch (same player + id + achieved_at)
        var distinctAchievements = DeduplicateAchievements(allAchievements);

        _logger.LogInformation("Processed {RoundCount} rounds individually, generated {AchievementCount} achievements (after dedup: {DistinctCount})",
            rounds.Count, allAchievements.Count, distinctAchievements.Count);

        return distinctAchievements;
    }

    /// <summary>
    /// Batch process achievements for better performance with large datasets
    /// Uses ClickHouse player_metrics for more efficient calculations
    /// </summary>
    private async Task<List<Achievement>> ProcessAchievementsForRoundsBatchAsync(List<PlayerRound> rounds)
    {
        var allAchievements = new List<Achievement>();

        try
        {
            _logger.LogInformation("Using batch processing for {RoundCount} rounds with max concurrency: {MaxConcurrency}",
                rounds.Count, _maxConcurrentRounds);

            // 1. Process kill streaks individually (they're round-specific) with throttling
            var streakTasks = rounds.Select(async round =>
            {
                await _concurrencyThrottle.WaitAsync();
                try
                {
                    return await _killStreakDetector.CalculateKillStreaksForRoundAsync(round);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing kill streaks for round {RoundId}", round.RoundId);
                    return new List<Achievement>();
                }
                finally
                {
                    _concurrencyThrottle.Release();
                }
            });

            var streakResults = await Task.WhenAll(streakTasks);
            allAchievements.AddRange(streakResults.SelectMany(r => r));

            // 2. Process milestones individually with throttling
            var milestoneTasks = rounds.Select(async round =>
            {
                await _concurrencyThrottle.WaitAsync();
                try
                {
                    return await _milestoneCalculator.CheckMilestoneCrossedAsync(round);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing milestones for round {RoundId}", round.RoundId);
                    return new List<Achievement>();
                }
                finally
                {
                    _concurrencyThrottle.Release();
                }
            });

            var milestoneResults = await Task.WhenAll(milestoneTasks);
            allAchievements.AddRange(milestoneResults.SelectMany(r => r));

            // 3. Process performance badges in batch (more efficient)
            try
            {
                var performanceAchievements = await _performanceBadgeCalculator.ProcessPerformanceBadgesBatchAsync(rounds);
                allAchievements.AddRange(performanceAchievements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing performance badges in batch");
            }

            _logger.LogInformation("Batch processed {RoundCount} rounds, generated {AchievementCount} achievements " +
                "({StreakCount} streaks, {MilestoneCount} milestones, {PerformanceCount} performance)",
                rounds.Count, allAchievements.Count,
                streakResults.Sum(r => r.Count),
                milestoneResults.Sum(r => r.Count),
                allAchievements.Count - streakResults.Sum(r => r.Count) - milestoneResults.Sum(r => r.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch processing achievements");
            throw;
        }

        // Deduplicate before returning to avoid inserting duplicates in the same batch
        var distinctAchievements = DeduplicateAchievements(allAchievements);
        return distinctAchievements;
    }

    /// <summary>
    /// Get comprehensive achievement summary for a player
    /// </summary>
    public async Task<PlayerAchievementSummary> GetPlayerAchievementSummaryAsync(string playerName)
    {
        try
        {
            // Get all achievements for the player
            var allAchievements = await _gamificationService.GetPlayerAchievementsAsync(playerName, 1000);

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
                .Where(a => a.AchievementType is AchievementTypes.Milestone or AchievementTypes.Placement)
                .OrderByDescending(a => a.Value)
                .ToList();

            var teamVictories = allAchievements
                .Where(a => a.AchievementType is AchievementTypes.TeamVictory or AchievementTypes.TeamVictorySwitched)
                .OrderByDescending(a => a.AchievedAt)
                .ToList();

            return new PlayerAchievementSummary
            {
                PlayerName = playerName,
                RecentAchievements = recentAchievements,
                AllBadges = badges,
                Milestones = milestones,
                TeamVictories = teamVictories,
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
                "kill_streaks" => await _gamificationService.GetKillStreakLeaderboardAsync(limit),
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

    public Task<PlayerPlacementSummary> GetPlayerPlacementSummaryAsync(string playerName, string? serverGuid = null, string? mapName = null)
    {
        return _gamificationService.GetPlayerPlacementSummaryAsync(playerName, serverGuid, mapName);
    }

    public async Task<List<PlacementLeaderboardEntry>> GetPlacementLeaderboardAsync(string? serverGuid = null, string? mapName = null, int limit = 100)
    {
        return await _gamificationService.GetPlacementLeaderboardAsync(serverGuid, mapName, limit);
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
        return await _gamificationService.PlayerHasAchievementAsync(playerName, achievementId);
    }

    /// <summary>
    /// Get all achievements with pagination, filtering, and player achievement IDs
    /// </summary>
    private static List<Achievement> DeduplicateAchievements(IEnumerable<Achievement> achievements)
    {
        return achievements
            .GroupBy(a => new { a.PlayerName, a.AchievementId, a.AchievedAt })
            .Select(g => g.First())
            .ToList();
    }

    public async Task<AchievementResponse> GetAllAchievementsWithPlayerIdsAsync(
        int page,
        int pageSize,
        string sortBy = "AchievedAt",
        string sortOrder = "desc",
        string? playerName = null,
        string? achievementType = null,
        string? achievementId = null,
        string? tier = null,
        DateTime? achievedFrom = null,
        DateTime? achievedTo = null,
        string? serverGuid = null,
        string? mapName = null)
    {
        try
        {
            var (achievements, totalCount) = await _gamificationService.GetAllAchievementsWithPagingAsync(
                page, pageSize, sortBy, sortOrder, playerName, achievementType,
                achievementId, tier, achievedFrom, achievedTo, serverGuid, mapName);

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            // Get player achievement IDs if a player name is specified
            var playerAchievementIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                playerAchievementIds = await _gamificationService.GetPlayerAchievementIdsAsync(playerName);
            }

            // Get labeled achievement information for the player's achievements
            var playerAchievementLabels = _achievementLabelingService.GetAchievementLabels(playerAchievementIds);

            return new AchievementResponse
            {
                Items = achievements,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalCount,
                TotalPages = totalPages,
                PlayerName = playerName,
                PlayerAchievementLabels = playerAchievementLabels
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting achievements with player IDs");
            throw;
        }
    }

    public void Dispose()
    {
        _concurrencyThrottle?.Dispose();
    }
}
