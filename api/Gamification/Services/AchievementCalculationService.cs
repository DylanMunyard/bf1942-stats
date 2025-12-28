using api.Gamification.Models;
using api.PlayerTracking;
using api.ClickHouse.Models;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace api.Gamification.Services;

/// <summary>
/// Background service for calculating achievements from new player rounds.
/// Runs regularly to process rounds that haven't been processed for achievements yet.
/// </summary>
public class AchievementCalculationService(
    SqliteGamificationService gamificationService,
    KillStreakDetector killStreakDetector,
    MilestoneCalculator milestoneCalculator,
    PlacementProcessor placementProcessor,
    TeamVictoryProcessor teamVictoryProcessor,
    PlayerTrackerDbContext dbContext,
    IConfiguration configuration,
    ILogger<AchievementCalculationService> logger)
{
    private readonly ILogger<AchievementCalculationService> _logger = logger;
    private readonly int _maxConcurrentRounds = configuration.GetValue<int>("GAMIFICATION_MAX_CONCURRENT_ROUNDS", 10);
    private SemaphoreSlim? _concurrencyThrottle;

    private SemaphoreSlim ConcurrencyThrottle => _concurrencyThrottle ??= new SemaphoreSlim(_maxConcurrentRounds, _maxConcurrentRounds);

    /// <summary>
    /// Process achievements for rounds that haven't been processed yet.
    /// This is the main entry point called by background jobs.
    /// </summary>
    public async Task ProcessNewAchievementsAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var activity = ActivitySources.Gamification.StartActivity("ProcessNewAchievements");
            activity?.SetTag("operation", "incremental_processing");

            // Get the last time we processed achievements
            var lastProcessed = await gamificationService.GetLastProcessedTimestampAsync();
            var now = DateTime.UtcNow;

            _logger.LogInformation("Starting achievement processing cycle. Last processed: {LastProcessed}", lastProcessed);

            // Get rounds since last processing
            var newRounds = await GetPlayerRoundsSinceAsync(lastProcessed);

            if (!newRounds.Any())
            {
                _logger.LogInformation("No new rounds to process for achievements");
                return;
            }

            activity?.SetTag("rounds.count", newRounds.Count);

            _logger.LogInformation("Processing achievements for {RoundCount} new rounds", newRounds.Count);

            // Calculate all achievements for these new rounds
            var allAchievements = await ProcessAchievementsForRounds(newRounds);

            // Process placements for rounds since last placement processed
            var lastPlacementProcessed = await gamificationService.GetLastPlacementProcessedTimestampAsync();
            var placementAchievements = await placementProcessor.ProcessPlacementsSinceAsync(lastPlacementProcessed);

            if (placementAchievements.Any())
            {
                allAchievements.AddRange(placementAchievements);
                activity?.SetTag("placements.count", placementAchievements.Count);
            }

            // Process team victory achievements
            var lastTeamVictoryProcessed = await gamificationService.GetLastTeamVictoryProcessedTimestampAsync();
            var teamVictoryAchievements = await teamVictoryProcessor.ProcessTeamVictoriesSinceAsync(lastTeamVictoryProcessed);

            if (teamVictoryAchievements.Any())
            {
                allAchievements.AddRange(teamVictoryAchievements);
                activity?.SetTag("team_victories.count", teamVictoryAchievements.Count);
            }

            // Store achievements in batch for efficiency
            if (allAchievements.Any())
            {
                await gamificationService.InsertAchievementsBatchAsync(allAchievements);
                activity?.SetTag("achievements.total", allAchievements.Count);
                _logger.LogInformation("Stored {AchievementCount} new achievements", allAchievements.Count);
            }

            stopwatch.Stop();
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            _logger.LogInformation(
                "Completed achievement processing cycle in {ElapsedMs}ms: {RoundCount} rounds, {AchievementCount} achievements",
                stopwatch.ElapsedMilliseconds, newRounds.Count, allAchievements.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing new achievements after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            ConcurrencyThrottle.Dispose();
        }
    }

    /// <summary>
    /// Process achievements for a specific set of rounds using PlayerSessions data
    /// </summary>
    public async Task<List<Achievement>> ProcessAchievementsForRounds(List<PlayerRound> rounds)
    {
        try
        {
            _logger.LogInformation("Processing achievements for {RoundCount} rounds", rounds.Count);

            // Use batch processing for better performance with large datasets
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

            try
            {
                // 1. Kill Streak Achievements
                var streakAchievements = await killStreakDetector.CalculateKillStreaksForRoundAsync(round);
                roundAchievements.AddRange(streakAchievements);

                // 2. Milestone Achievements
                var milestoneAchievements = await milestoneCalculator.CheckMilestoneCrossedAsync(round);
                roundAchievements.AddRange(milestoneAchievements);

                allAchievements.AddRange(roundAchievements);

                if (roundAchievements.Any())
                {
                    _logger.LogDebug("Round {RoundId} for {PlayerName}: {AchievementCount} achievements",
                        round.RoundId, round.PlayerName, roundAchievements.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing round {RoundId} for player {PlayerName}",
                    round.RoundId, round.PlayerName);
            }
        }

        // Deduplicate achievements generated in this batch
        var distinctAchievements = DeduplicateAchievements(allAchievements);

        _logger.LogInformation("Processed {RoundCount} rounds individually, generated {AchievementCount} achievements (after dedup: {DistinctCount})",
            rounds.Count, allAchievements.Count, distinctAchievements.Count);

        return distinctAchievements;
    }

    /// <summary>
    /// Batch process achievements for better performance with large datasets
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
                await ConcurrencyThrottle.WaitAsync();
                try
                {
                    return await killStreakDetector.CalculateKillStreaksForRoundAsync(round);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing kill streaks for round {RoundId}", round.RoundId);
                    return new List<Achievement>();
                }
                finally
                {
                    ConcurrencyThrottle.Release();
                }
            });

            var streakResults = await Task.WhenAll(streakTasks);
            allAchievements.AddRange(streakResults.SelectMany(r => r));

            // 2. Process milestones individually with throttling
            var milestoneTasks = rounds.Select(async round =>
            {
                await ConcurrencyThrottle.WaitAsync();
                try
                {
                    return await milestoneCalculator.CheckMilestoneCrossedAsync(round);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing milestones for round {RoundId}", round.RoundId);
                    return new List<Achievement>();
                }
                finally
                {
                    ConcurrencyThrottle.Release();
                }
            });

            var milestoneResults = await Task.WhenAll(milestoneTasks);
            allAchievements.AddRange(milestoneResults.SelectMany(r => r));

            _logger.LogInformation("Batch processed {RoundCount} rounds, generated {AchievementCount} achievements " +
                "({StreakCount} streaks, {MilestoneCount} milestones)",
                rounds.Count, allAchievements.Count,
                streakResults.Sum(r => r.Count),
                milestoneResults.Sum(r => r.Count));
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
    /// Get player rounds since the specified timestamp from SQLite PlayerSessions
    /// </summary>
    private async Task<List<PlayerRound>> GetPlayerRoundsSinceAsync(DateTime sinceTime)
    {
        try
        {
            // Query PlayerSessions that have completed (have RoundId and are closed)
            var sessions = await dbContext.PlayerSessions
                .Include(ps => ps.Player)
                .Where(ps => ps.StartTime >= sinceTime &&
                            ps.RoundId != null &&
                            !ps.IsActive &&
                            ps.TotalKills >= 0) // Basic validation
                .OrderBy(ps => ps.StartTime)
                .ToListAsync();

            // Convert PlayerSessions to PlayerRound format expected by achievement calculators
            var rounds = sessions.Select(MapPlayerSessionToPlayerRound).ToList();

            _logger.LogInformation("Found {SessionCount} player sessions since {SinceTime}, converted to {RoundCount} rounds",
                sessions.Count, sinceTime, rounds.Count);

            return rounds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player rounds since {SinceTime}", sinceTime);
            return new List<PlayerRound>();
        }
    }

    /// <summary>
    /// Map PlayerSession to PlayerRound for achievement processing
    /// </summary>
    private static PlayerRound MapPlayerSessionToPlayerRound(PlayerSession session)
    {
        return new PlayerRound
        {
            PlayerName = session.PlayerName,
            RoundId = session.RoundId!,
            RoundStartTime = session.StartTime,
            RoundEndTime = session.LastSeenTime,
            FinalScore = session.TotalScore,
            FinalKills = (uint)session.TotalKills,
            FinalDeaths = (uint)session.TotalDeaths,
            PlayTimeMinutes = (session.LastSeenTime - session.StartTime).TotalMinutes,
            ServerGuid = session.ServerGuid,
            MapName = session.MapName,
            TeamLabel = session.CurrentTeamLabel,
            GameId = session.GameType,
            IsBot = session.Player?.AiBot ?? false,
            AveragePing = session.AveragePing
        };
    }

    /// <summary>
    /// Deduplicate achievements by player, achievement ID, and achieved time
    /// </summary>
    private static List<Achievement> DeduplicateAchievements(IEnumerable<Achievement> achievements)
    {
        return achievements
            .GroupBy(a => new { a.PlayerName, a.AchievementId, a.AchievedAt })
            .Select(g => g.First())
            .ToList();
    }
}