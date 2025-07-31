using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace junie_des_1942stats.Gamification.Services;

public class KillStreakDetector
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClickHouseGamificationService _readService;
    private readonly BadgeDefinitionsService _badgeService;
    private readonly ILogger<KillStreakDetector> _logger;

    public KillStreakDetector(
        IServiceScopeFactory scopeFactory,
        [FromKeyedServices("read")] ClickHouseGamificationService readService,
        BadgeDefinitionsService badgeService,
        ILogger<KillStreakDetector> logger)
    {
        _scopeFactory = scopeFactory;
        _readService = readService;
        _badgeService = badgeService;
        _logger = logger;
    }

    public async Task<List<Achievement>> CalculateKillStreaksForRoundAsync(PlayerRound round)
    {
        var achievements = new List<Achievement>();

        try
        {
            // Get player observations for this round, ordered by timestamp
            var observations = await GetPlayerObservationsForRound(round.RoundId, round.PlayerName);
            
            if (observations.Count < 2) return achievements; // Need at least 2 observations to detect streaks

            // Calculate streaks and track when each threshold was achieved
            var streakThresholds = CalculateStreakThresholds(observations);
            
            if (!streakThresholds.Any()) return achievements; // No streaks of 5+ kills

            // Check achievement thresholds: 5, 10, 15, 20, 25, 30, 50+
            var thresholds = new[] { 5, 10, 15, 20, 25, 30, 50 };
            
            foreach (var threshold in thresholds)
            {
                if (streakThresholds.TryGetValue(threshold, out var achievementTime))
                {
                    var achievementId = $"kill_streak_{threshold}";
                    
                    // Check if player already has this achievement for this specific round/streak
                    // Use a more specific check that considers the round context and achievement time
                    var existingAchievement = await _readService.GetPlayerAchievementsByTypeAsync(round.PlayerName, AchievementTypes.KillStreak);
                    var hasAchievementForThisStreak = existingAchievement.Any(a => 
                        a.AchievementId == achievementId && 
                        a.RoundId == round.RoundId && 
                        a.AchievedAt == achievementTime);
                    
                    if (!hasAchievementForThisStreak)
                    {
                        var badgeDefinition = _badgeService.GetBadgeDefinition(achievementId);
                        if (badgeDefinition != null)
                        {
                            achievements.Add(new Achievement
                            {
                                PlayerName = round.PlayerName,
                                AchievementType = AchievementTypes.KillStreak,
                                AchievementId = achievementId,
                                AchievementName = badgeDefinition.Name,
                                Tier = badgeDefinition.Tier,
                                Value = (uint)threshold,
                                AchievedAt = achievementTime,
                                ProcessedAt = DateTime.UtcNow,
                                ServerGuid = round.ServerGuid,
                                MapName = round.MapName,
                                RoundId = round.RoundId,
                                Metadata = $"{{\"actual_streak\":{threshold},\"round_kills\":{round.Kills}}}"
                            });

                            _logger.LogInformation("Kill streak achievement: {PlayerName} achieved {AchievementName} with {Threshold} kills at {AchievementTime}",
                                round.PlayerName, badgeDefinition.Name, threshold, achievementTime);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Skipping duplicate kill streak achievement: {PlayerName} already has {AchievementId} for round {RoundId} at {AchievementTime}",
                            round.PlayerName, achievementId, round.RoundId, achievementTime);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating kill streaks for round {RoundId}, player {PlayerName}",
                round.RoundId, round.PlayerName);
        }

        return achievements;
    }

    /// <summary>
    /// Calculate when each streak threshold was achieved during the observations
    /// </summary>
    private Dictionary<int, DateTime> CalculateStreakThresholds(List<PlayerObservation> observations)
    {
        var thresholds = new Dictionary<int, DateTime>();
        var thresholdValues = new[] { 5, 10, 15, 20, 25, 30, 50 };
        
        if (observations.Count < 2) return thresholds;

        int currentStreak = 0;
        int previousKills = observations.First().Kills;
        int previousDeaths = observations.First().Deaths;

        foreach (var observation in observations.Skip(1))
        {
            var killsDiff = observation.Kills - previousKills;
            var deathsDiff = observation.Deaths - previousDeaths;

            if (killsDiff > 0 && deathsDiff == 0)
            {
                // Player got kills without dying - continue/start streak
                currentStreak += killsDiff;
                
                // Check if any thresholds were reached during this observation
                foreach (var threshold in thresholdValues)
                {
                    if (currentStreak >= threshold && !thresholds.ContainsKey(threshold))
                    {
                        // This is the first time this threshold was reached
                        thresholds[threshold] = observation.Timestamp;
                    }
                }
            }
            else if (deathsDiff > 0)
            {
                // Player died - reset streak
                currentStreak = 0;
            }

            previousKills = observation.Kills;
            previousDeaths = observation.Deaths;
        }

        return thresholds;
    }

    private async Task<List<PlayerObservation>> GetPlayerObservationsForRound(string roundId, string playerName)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
        
        // Get all observations for this player in this round, ordered by timestamp
        var observations = await dbContext.PlayerObservations
            .Include(o => o.Session)
            .Where(o => o.Session.PlayerName == playerName)
            .Where(o => o.Session.SessionId.ToString().Contains(roundId) || 
                       (o.Session.StartTime <= DateTime.UtcNow && o.Session.LastSeenTime >= DateTime.UtcNow.AddHours(-6)))
            .OrderBy(o => o.Timestamp)
            .ToListAsync();

        // Filter to observations that likely belong to this round
        // Since we don't have a direct round_id in observations, we'll use session timing
        if (observations.Any())
        {
            var firstObs = observations.First();
            var lastObs = observations.Last();
            var roundDuration = lastObs.Timestamp - firstObs.Timestamp;
            
            // Filter out observations that are too far apart (likely different rounds)
            if (roundDuration.TotalHours > 2) // Reasonable max round duration
            {
                observations = observations
                    .Where(o => o.Timestamp >= lastObs.Timestamp.AddHours(-2))
                    .ToList();
            }
        }

        return observations;
    }

    private int CalculateMaxConsecutiveKills(List<PlayerObservation> observations)
    {
        if (observations.Count < 2) return 0;

        int maxStreak = 0;
        int currentStreak = 0;
        int previousKills = observations.First().Kills;
        int previousDeaths = observations.First().Deaths;

        foreach (var observation in observations.Skip(1))
        {
            var killsDiff = observation.Kills - previousKills;
            var deathsDiff = observation.Deaths - previousDeaths;

            if (killsDiff > 0 && deathsDiff == 0)
            {
                // Player got kills without dying - continue/start streak
                currentStreak += killsDiff;
                maxStreak = Math.Max(maxStreak, currentStreak);
            }
            else if (deathsDiff > 0)
            {
                // Player died - reset streak
                currentStreak = 0;
            }

            previousKills = observation.Kills;
            previousDeaths = observation.Deaths;
        }

        return maxStreak;
    }

    public async Task<KillStreakStats> GetPlayerKillStreakStatsAsync(string playerName)
    {
        try
        {
            // Get all kill streak achievements for this player
            var streakAchievements = await _readService.GetPlayerAchievementsByTypeAsync(
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
                BestStreakServer = await GetServerName(bestAchievement.ServerGuid),
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
            _logger.LogError(ex, "Error getting kill streak stats for player {PlayerName}", playerName);
            return new KillStreakStats();
        }
    }

    private async Task<string> GetServerName(string serverGuid)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
            
            var server = await dbContext.Servers
                .Where(s => s.Guid == serverGuid)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
            
            return server ?? "Unknown Server";
        }
        catch
        {
            return "Unknown Server";
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
            _logger.LogInformation("Processed {RoundCount} rounds, found {AchievementCount} kill streak achievements",
                rounds.Count, allAchievements.Count);
        }

        return allAchievements;
    }
} 