using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class KillStreakDetector
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ClickHouseGamificationService _clickHouseService;
    private readonly BadgeDefinitionsService _badgeService;
    private readonly ILogger<KillStreakDetector> _logger;

    public KillStreakDetector(
        PlayerTrackerDbContext dbContext,
        ClickHouseGamificationService clickHouseService,
        BadgeDefinitionsService badgeService,
        ILogger<KillStreakDetector> logger)
    {
        _dbContext = dbContext;
        _clickHouseService = clickHouseService;
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

            // Calculate max consecutive kills in this round
            var maxStreak = CalculateMaxConsecutiveKills(observations);
            
            if (maxStreak < 5) return achievements; // Only award achievements for 5+ kill streaks

            // Check achievement thresholds: 5, 10, 15, 20, 25, 30, 50+
            var thresholds = new[] { 5, 10, 15, 20, 25, 30, 50 };
            
            foreach (var threshold in thresholds.Where(t => maxStreak >= t))
            {
                // Only award if this is their first time hitting this threshold
                var hasAchievement = await _clickHouseService.PlayerHasAchievementAsync(
                    round.PlayerName, $"kill_streak_{threshold}");
                
                if (!hasAchievement)
                {
                    var badgeDefinition = _badgeService.GetBadgeDefinition($"kill_streak_{threshold}");
                    if (badgeDefinition != null)
                    {
                        achievements.Add(new Achievement
                        {
                            PlayerName = round.PlayerName,
                            AchievementType = AchievementTypes.KillStreak,
                            AchievementId = $"kill_streak_{threshold}",
                            AchievementName = badgeDefinition.Name,
                            Tier = badgeDefinition.Tier,
                            Value = (uint)threshold,
                            AchievedAt = round.RoundEndTime,
                            ProcessedAt = DateTime.UtcNow,
                            ServerGuid = round.ServerGuid,
                            MapName = round.MapName,
                            RoundId = round.RoundId,
                            Metadata = $"{{\"actual_streak\":{maxStreak},\"round_kills\":{round.Kills}}}"
                        });

                        _logger.LogInformation("Kill streak achievement: {PlayerName} achieved {AchievementName} with {MaxStreak} kills",
                            round.PlayerName, badgeDefinition.Name, maxStreak);
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

    private async Task<List<PlayerObservation>> GetPlayerObservationsForRound(string roundId, string playerName)
    {
        // Get all observations for this player in this round, ordered by timestamp
        var observations = await _dbContext.PlayerObservations
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
            var streakAchievements = await _clickHouseService.GetPlayerAchievementsByTypeAsync(
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
            var server = await _dbContext.Servers
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