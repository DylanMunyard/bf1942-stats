using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.PlayerStats.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace junie_des_1942stats.ClickHouse;

/// <summary>
/// Service for calculating player progression metrics and deltas for V2 API
/// Focuses on engagement and progression analysis using ClickHouse analytics
/// </summary>
public class PlayerProgressionService : BaseClickHouseService, IClickHouseReader
{
    private readonly ILogger<PlayerProgressionService> _logger;

    public PlayerProgressionService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerProgressionService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteQueryAsync(string query)
    {
        return await ExecuteQueryInternalAsync(query);
    }

    /// <summary>
    /// Get comprehensive progression details for a player
    /// </summary>
    public async Task<PlayerProgressionDetails> GetPlayerProgressionAsync(string playerName)
    {
        var result = new PlayerProgressionDetails
        {
            PlayerName = playerName,
            AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
            AnalysisPeriodEnd = DateTime.UtcNow
        };

        try
        {
            // Execute all analysis tasks in parallel for better performance
            var tasks = new[]
            {
                GetOverallProgressionAsync(playerName),
                GetMapProgressionsAsync(playerName),
                GetServerRankingProgressionsAsync(playerName),
                GetPerformanceTrajectoryAsync(playerName),
                GetRecentActivityAsync(playerName),
                GetComparativeMetricsAsync(playerName)
            };

            var results = await Task.WhenAll(tasks);

            result.OverallProgression = results[0] as OverallProgression ?? new();
            result.MapProgressions = results[1] as List<MapProgression> ?? new();
            result.ServerRankings = results[2] as List<ServerRankingProgression> ?? new();
            result.PerformanceTrajectory = results[3] as PerformanceTrajectory ?? new();
            result.RecentActivity = results[4] as RecentActivity ?? new();
            result.ComparativeMetrics = results[5] as ComparativeMetrics ?? new();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player progression for {PlayerName}", playerName);
            return result;
        }
    }

    private async Task<object> GetOverallProgressionAsync(string playerName)
    {
        var progression = new OverallProgression();

        var query = $@"
WITH current_period AS (
    SELECT 
        SUM(final_kills) as kills,
        SUM(final_deaths) as deaths,
        SUM(play_time_minutes) as play_time,
        SUM(final_score) as score
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 30 DAY
),
previous_period AS (
    SELECT 
        SUM(final_kills) as kills,
        SUM(final_deaths) as deaths,
        SUM(play_time_minutes) as play_time,
        SUM(final_score) as score
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 60 DAY
      AND round_start_time < now() - INTERVAL 30 DAY
),
milestones AS (
    SELECT 
        SUM(final_kills) as total_kills,
        SUM(final_score) as total_score,
        SUM(play_time_minutes) as total_play_time
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
)
SELECT 
    -- Current metrics
    c.kills / nullIf(c.play_time, 0) as current_kill_rate,
    c.kills / nullIf(c.deaths, 0) as current_kd_ratio,
    c.score / nullIf(c.play_time, 0) as current_score_per_minute,
    
    -- Previous metrics
    p.kills / nullIf(p.play_time, 0) as previous_kill_rate,
    p.kills / nullIf(p.deaths, 0) as previous_kd_ratio,
    p.score / nullIf(p.play_time, 0) as previous_score_per_minute,
    
    -- Milestone data
    m.total_kills,
    m.total_score,
    m.total_play_time
FROM current_period c
CROSS JOIN previous_period p
CROSS JOIN milestones m
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            var parts = lines[0].Split('\t');
            if (parts.Length >= 9)
            {
                progression.CurrentKillRate = ParseDouble(parts[0]);
                progression.CurrentKDRatio = ParseDouble(parts[1]);
                progression.CurrentScorePerMinute = ParseDouble(parts[2]);

                progression.KillRateDelta = new ProgressionDelta
                {
                    CurrentValue = progression.CurrentKillRate,
                    PreviousValue = ParseDouble(parts[3])
                };

                progression.KDRatioDelta = new ProgressionDelta
                {
                    CurrentValue = progression.CurrentKDRatio,
                    PreviousValue = ParseDouble(parts[4])
                };

                progression.ScorePerMinuteDelta = new ProgressionDelta
                {
                    CurrentValue = progression.CurrentScorePerMinute,
                    PreviousValue = ParseDouble(parts[5])
                };

                // Calculate milestone progress
                var totalKills = ParseInt(parts[6]);
                var totalScore = ParseInt(parts[7]);
                var totalPlayTime = ParseDouble(parts[8]);

                progression.ActiveMilestones = CalculateMilestoneProgress(totalKills, totalScore, totalPlayTime);
            }
        }

        // Get recent achievements
        progression.RecentAchievements = await GetRecentAchievementsAsync(playerName);

        return progression;
    }

    private async Task<object> GetMapProgressionsAsync(string playerName)
    {
        var progressions = new List<MapProgression>();

        var query = $@"
WITH current_period AS (
    SELECT 
        map_name,
        COUNT(*) as rounds,
        SUM(play_time_minutes) as play_time,
        SUM(final_kills) as kills,
        SUM(final_deaths) as deaths,
        AVG(final_score) as avg_score
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 30 DAY
    GROUP BY map_name
),
previous_period AS (
    SELECT 
        map_name,
        SUM(final_kills) as kills,
        SUM(final_deaths) as deaths,
        SUM(play_time_minutes) as play_time
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 60 DAY
      AND round_start_time < now() - INTERVAL 30 DAY
    GROUP BY map_name
),
map_averages AS (
    SELECT 
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_kills / nullIf(final_deaths, 0)) as avg_kd_ratio
    FROM player_rounds
    WHERE round_start_time >= now() - INTERVAL 90 DAY
      AND play_time_minutes > 5
    GROUP BY map_name
)
SELECT 
    c.map_name,
    c.rounds,
    c.play_time / 60 as play_time_hours,
    c.kills / nullIf(c.play_time, 0) as current_kill_rate,
    c.kills / nullIf(c.deaths, 0) as current_kd_ratio,
    p.kills / nullIf(p.play_time, 0) as previous_kill_rate,
    p.kills / nullIf(p.deaths, 0) as previous_kd_ratio,
    a.avg_kill_rate as map_avg_kill_rate,
    a.avg_kd_ratio as map_avg_kd_ratio
FROM current_period c
LEFT JOIN previous_period p ON c.map_name = p.map_name
LEFT JOIN map_averages a ON c.map_name = a.map_name
WHERE c.rounds >= 3  -- Only include maps with reasonable sample size
ORDER BY c.rounds DESC
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 9)
            {
                var mapProgression = new MapProgression
                {
                    MapName = parts[0],
                    TotalRoundsPlayed = ParseInt(parts[1]),
                    TotalPlayTimeHours = ParseDouble(parts[2]),
                    CurrentKillRate = ParseDouble(parts[3]),
                    CurrentKDRatio = ParseDouble(parts[4]),
                    MapAverageKillRate = ParseDouble(parts[7]),
                    MapAverageKDRatio = ParseDouble(parts[8])
                };

                mapProgression.KillRateDelta = new ProgressionDelta
                {
                    CurrentValue = mapProgression.CurrentKillRate,
                    PreviousValue = ParseDouble(parts[5])
                };

                mapProgression.KDRatioDelta = new ProgressionDelta
                {
                    CurrentValue = mapProgression.CurrentKDRatio,
                    PreviousValue = ParseDouble(parts[6])
                };

                // Calculate performance rating
                mapProgression.PerformanceVsAverage = CalculatePerformanceRating(
                    mapProgression.CurrentKillRate, mapProgression.MapAverageKillRate,
                    mapProgression.CurrentKDRatio, mapProgression.MapAverageKDRatio);

                // Get performance trend for this map
                mapProgression.PerformanceTrend = await GetMapPerformanceTrendAsync(playerName, mapProgression.MapName);

                progressions.Add(mapProgression);
            }
        }

        return progressions;
    }

    private async Task<object> GetServerRankingProgressionsAsync(string playerName)
    {
        var rankings = new List<ServerRankingProgression>();

        // This would need to integrate with existing ranking system
        // For now, return empty list - would need to query server rankings table
        
        return rankings;
    }

    private async Task<object> GetPerformanceTrajectoryAsync(string playerName)
    {
        var trajectory = new PerformanceTrajectory();

        // Get daily performance data for the last 90 days
        var query = $@"
SELECT 
    toDate(round_start_time) as date,
    SUM(final_kills) / nullIf(SUM(play_time_minutes), 0) as kill_rate,
    SUM(final_kills) / nullIf(SUM(final_deaths), 0) as kd_ratio,
    SUM(final_score) / nullIf(SUM(play_time_minutes), 0) as score_per_minute,
    COUNT(*) as sample_size
FROM player_rounds
WHERE player_name = '{playerName.Replace("'", "''")}'
  AND round_start_time >= now() - INTERVAL 90 DAY
  AND play_time_minutes > 5
GROUP BY date
HAVING sample_size >= 3  -- At least 3 rounds per day for reliable data
ORDER BY date
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var killRatePoints = new List<PerformanceDataPoint>();
        var kdRatioPoints = new List<PerformanceDataPoint>();
        var scorePoints = new List<PerformanceDataPoint>();

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 5 && DateTime.TryParse(parts[0], out var date))
            {
                var killRate = ParseDouble(parts[1]);
                var kdRatio = ParseDouble(parts[2]);
                var scorePerMinute = ParseDouble(parts[3]);
                var sampleSize = ParseInt(parts[4]);

                killRatePoints.Add(new PerformanceDataPoint 
                { 
                    Date = date, 
                    Value = killRate, 
                    SampleSize = sampleSize 
                });
                
                kdRatioPoints.Add(new PerformanceDataPoint 
                { 
                    Date = date, 
                    Value = kdRatio, 
                    SampleSize = sampleSize 
                });
                
                scorePoints.Add(new PerformanceDataPoint 
                { 
                    Date = date, 
                    Value = scorePerMinute, 
                    SampleSize = sampleSize 
                });
            }
        }

        trajectory.KillRateTrajectory = killRatePoints;
        trajectory.KDRatioTrajectory = kdRatioPoints;
        trajectory.ScoreTrajectory = scorePoints;

        // Calculate trend analysis
        trajectory.KillRateTrend = CalculateTrendAnalysis(killRatePoints);
        trajectory.KDRatioTrend = CalculateTrendAnalysis(kdRatioPoints);
        trajectory.ScoreTrend = CalculateTrendAnalysis(scorePoints);

        // Determine overall trajectory
        trajectory.OverallTrajectory = DetermineOverallTrajectory(
            trajectory.KillRateTrend, trajectory.KDRatioTrend, trajectory.ScoreTrend);

        trajectory.TrajectoryConfidence = CalculateTrajectoryConfidence(
            trajectory.KillRateTrend, trajectory.KDRatioTrend, trajectory.ScoreTrend);

        trajectory.TrajectoryDescription = GenerateTrajectoryDescription(trajectory);

        return trajectory;
    }

    private async Task<object> GetRecentActivityAsync(string playerName)
    {
        var activity = new RecentActivity();

        var query = $@"
WITH recent_stats AS (
    SELECT 
        MAX(round_start_time) as last_played,
        COUNT(*) as rounds_7d,
        SUM(play_time_minutes) as playtime_7d,
        groupUniqArray(server_guid) as servers_7d,
        groupUniqArray(map_name) as maps_7d
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 7 DAY
),
daily_activity AS (
    SELECT 
        toDate(round_start_time) as date,
        COUNT(*) as rounds,
        SUM(play_time_minutes) as playtime
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 30 DAY
    GROUP BY date
    ORDER BY date
),
hourly_patterns AS (
    SELECT 
        toHour(round_start_time) as hour,
        COUNT(*) as frequency
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 30 DAY
    GROUP BY hour
    HAVING frequency >= 5  -- At least 5 sessions in that hour
    ORDER BY frequency DESC
    LIMIT 6
)
SELECT 
    r.last_played,
    dateDiff('day', r.last_played, now()) as days_since,
    r.rounds_7d,
    r.playtime_7d,
    length(r.servers_7d) as unique_servers_7d,
    length(r.maps_7d) as unique_maps_7d,
    groupArray(d.date) as dates,
    groupArray(d.rounds) as daily_rounds,
    groupArray(d.playtime) as daily_playtime,
    groupArray(h.hour) as preferred_hours
FROM recent_stats r
CROSS JOIN (SELECT groupArray(date) as dates, groupArray(rounds) as daily_rounds, groupArray(playtime) as daily_playtime FROM daily_activity) d
CROSS JOIN (SELECT groupArray(hour) as preferred_hours FROM hourly_patterns) h
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            var parts = lines[0].Split('\t');
            if (parts.Length >= 6)
            {
                if (DateTime.TryParse(parts[0], out var lastPlayed))
                {
                    activity.LastPlayedDate = lastPlayed;
                }
                activity.DaysSinceLastPlayed = ParseInt(parts[1]);
                activity.RoundsLast7Days = ParseInt(parts[2]);
                activity.PlayTimeLast7Days = ParseDouble(parts[3]);

                // Determine activity level
                activity.RecentActivityLevel = DetermineActivityLevel(activity.RoundsLast7Days, activity.DaysSinceLastPlayed);
            }
        }

        return activity;
    }

    private async Task<object> GetComparativeMetricsAsync(string playerName)
    {
        var comparative = new ComparativeMetrics();

        // Get global comparison
        var globalQuery = $@"
WITH player_stats AS (
    SELECT 
        SUM(final_kills) / nullIf(SUM(play_time_minutes), 0) as kill_rate,
        SUM(final_kills) / nullIf(SUM(final_deaths), 0) as kd_ratio,
        SUM(final_score) / nullIf(SUM(play_time_minutes), 0) as score_per_minute
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= now() - INTERVAL 90 DAY
),
global_stats AS (
    SELECT 
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_kills / nullIf(final_deaths, 0)) as avg_kd_ratio,
        AVG(final_score / nullIf(play_time_minutes, 0)) as avg_score_per_minute,
        uniq(player_name) as total_players
    FROM player_rounds
    WHERE round_start_time >= now() - INTERVAL 90 DAY
      AND play_time_minutes > 5
)
SELECT 
    p.kill_rate as player_kill_rate,
    p.kd_ratio as player_kd_ratio,
    p.score_per_minute as player_score_per_minute,
    g.avg_kill_rate as global_avg_kill_rate,
    g.avg_kd_ratio as global_avg_kd_ratio,
    g.avg_score_per_minute as global_avg_score_per_minute,
    g.total_players
FROM player_stats p
CROSS JOIN global_stats g
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(globalQuery);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            var parts = lines[0].Split('\t');
            if (parts.Length >= 7)
            {
                comparative.GlobalComparison = new GlobalComparison
                {
                    GlobalAverageKillRate = ParseDouble(parts[3]),
                    GlobalAverageKDRatio = ParseDouble(parts[4]),
                    GlobalAverageScorePerMinute = ParseDouble(parts[5]),
                    TotalPlayers = ParseInt(parts[6]),
                    KillRateRating = CalculateRating(ParseDouble(parts[0]), ParseDouble(parts[3])),
                    KDRatioRating = CalculateRating(ParseDouble(parts[1]), ParseDouble(parts[4])),
                    ScoreRating = CalculateRating(ParseDouble(parts[2]), ParseDouble(parts[5]))
                };
            }
        }

        return comparative;
    }

    private List<MilestoneProgress> CalculateMilestoneProgress(int totalKills, int totalScore, double totalPlayTime)
    {
        var milestones = new List<MilestoneProgress>();

        // Kill milestones
        var killMilestones = new[] { 1000, 5000, 10000, 25000, 50000, 100000 };
        foreach (var milestone in killMilestones)
        {
            if (totalKills < milestone)
            {
                milestones.Add(new MilestoneProgress
                {
                    MilestoneType = "kills",
                    MilestoneName = $"{milestone:N0} Kills",
                    TargetValue = milestone,
                    CurrentValue = totalKills,
                    ProgressDescription = $"{totalKills:N0} / {milestone:N0} kills"
                });
                break; // Only show next milestone
            }
        }

        // Play time milestones (in hours)
        var playTimeHours = (int)(totalPlayTime / 60);
        var timeMilestones = new[] { 10, 50, 100, 500, 1000 };
        foreach (var milestone in timeMilestones)
        {
            if (playTimeHours < milestone)
            {
                milestones.Add(new MilestoneProgress
                {
                    MilestoneType = "playtime",
                    MilestoneName = $"{milestone} Hours Played",
                    TargetValue = milestone,
                    CurrentValue = playTimeHours,
                    ProgressDescription = $"{playTimeHours} / {milestone} hours"
                });
                break;
            }
        }

        return milestones;
    }

    private async Task<List<RecentAchievement>> GetRecentAchievementsAsync(string playerName)
    {
        // This would query the achievements system - placeholder for now
        return new List<RecentAchievement>();
    }

    private async Task<List<PerformanceDataPoint>> GetMapPerformanceTrendAsync(string playerName, string mapName)
    {
        var query = $@"
SELECT 
    toDate(round_start_time) as date,
    AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_performance,
    COUNT(*) as sample_size
FROM player_rounds
WHERE player_name = '{playerName.Replace("'", "''")}'
  AND map_name = '{mapName.Replace("'", "''")}'
  AND round_start_time >= now() - INTERVAL 60 DAY
  AND play_time_minutes > 5
GROUP BY date
HAVING sample_size >= 2
ORDER BY date DESC
LIMIT 30
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var points = new List<PerformanceDataPoint>();

        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 && DateTime.TryParse(parts[0], out var date))
            {
                points.Add(new PerformanceDataPoint
                {
                    Date = date,
                    Value = ParseDouble(parts[1]),
                    SampleSize = ParseInt(parts[2])
                });
            }
        }

        return points;
    }

    private PlayerPerformanceRating CalculatePerformanceRating(double playerKillRate, double avgKillRate, double playerKDRatio, double avgKDRatio)
    {
        // Combined rating based on both metrics
        var killRateRatio = avgKillRate > 0 ? playerKillRate / avgKillRate : 1;
        var kdRatioRatio = avgKDRatio > 0 ? playerKDRatio / avgKDRatio : 1;
        var combinedRatio = (killRateRatio + kdRatioRatio) / 2;

        return combinedRatio switch
        {
            >= 1.5 => PlayerPerformanceRating.Exceptional,
            >= 1.15 => PlayerPerformanceRating.AboveAverage,
            >= 0.85 => PlayerPerformanceRating.Average,
            >= 0.6 => PlayerPerformanceRating.BelowAverage,
            _ => PlayerPerformanceRating.Poor
        };
    }

    private PlayerPerformanceRating CalculateRating(double playerValue, double averageValue)
    {
        if (averageValue <= 0) return PlayerPerformanceRating.Average;
        
        var ratio = playerValue / averageValue;
        return ratio switch
        {
            >= 1.75 => PlayerPerformanceRating.Exceptional,
            >= 1.25 => PlayerPerformanceRating.AboveAverage,
            >= 0.8 => PlayerPerformanceRating.Average,
            >= 0.5 => PlayerPerformanceRating.BelowAverage,
            _ => PlayerPerformanceRating.Poor
        };
    }

    private TrendAnalysis CalculateTrendAnalysis(List<PerformanceDataPoint> points)
    {
        if (points.Count < 3)
        {
            return new TrendAnalysis
            {
                Trend = TrajectoryDirection.Stable,
                TrendDescription = "Insufficient data for trend analysis",
                Slope = 0,
                RSquared = 0
            };
        }

        // Simple linear regression
        var n = points.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            var x = i; // Use index as x-value
            var y = points[i].Value;
            
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        
        // Calculate R-squared
        var yMean = sumY / n;
        var ssRes = 0.0;
        var ssTot = 0.0;
        
        for (int i = 0; i < n; i++)
        {
            var predictedY = slope * i + (sumY - slope * sumX) / n;
            ssRes += Math.Pow(points[i].Value - predictedY, 2);
            ssTot += Math.Pow(points[i].Value - yMean, 2);
        }
        
        var rSquared = ssTot > 0 ? 1 - (ssRes / ssTot) : 0;

        var trend = slope switch
        {
            > 0.1 => TrajectoryDirection.StronglyImproving,
            > 0.02 => TrajectoryDirection.Improving,
            < -0.1 => TrajectoryDirection.StronglyDeclining,
            < -0.02 => TrajectoryDirection.Declining,
            _ => TrajectoryDirection.Stable
        };

        return new TrendAnalysis
        {
            Slope = slope,
            RSquared = Math.Max(0, rSquared),
            Trend = trend,
            TrendDescription = GenerateTrendDescription(trend, rSquared)
        };
    }

    private TrajectoryDirection DetermineOverallTrajectory(params TrendAnalysis[] trends)
    {
        var improvingCount = trends.Count(t => t.Trend == TrajectoryDirection.Improving || t.Trend == TrajectoryDirection.StronglyImproving);
        var decliningCount = trends.Count(t => t.Trend == TrajectoryDirection.Declining || t.Trend == TrajectoryDirection.StronglyDeclining);
        var stronglyImprovingCount = trends.Count(t => t.Trend == TrajectoryDirection.StronglyImproving);
        var stronglyDecliningCount = trends.Count(t => t.Trend == TrajectoryDirection.StronglyDeclining);

        if (stronglyImprovingCount >= 2) return TrajectoryDirection.StronglyImproving;
        if (stronglyDecliningCount >= 2) return TrajectoryDirection.StronglyDeclining;
        if (improvingCount > decliningCount) return TrajectoryDirection.Improving;
        if (decliningCount > improvingCount) return TrajectoryDirection.Declining;
        return TrajectoryDirection.Stable;
    }

    private double CalculateTrajectoryConfidence(params TrendAnalysis[] trends)
    {
        if (trends.Length == 0) return 0;
        return trends.Average(t => t.RSquared);
    }

    private string GenerateTrajectoryDescription(PerformanceTrajectory trajectory)
    {
        var confidence = trajectory.TrajectoryConfidence > 0.7 ? "strong" : 
                        trajectory.TrajectoryConfidence > 0.4 ? "moderate" : "weak";

        return trajectory.OverallTrajectory switch
        {
            TrajectoryDirection.StronglyImproving => $"Player shows {confidence} evidence of significant improvement across multiple metrics",
            TrajectoryDirection.Improving => $"Player demonstrates {confidence} upward trend in performance",
            TrajectoryDirection.Declining => $"Player shows {confidence} downward trend in performance",
            TrajectoryDirection.StronglyDeclining => $"Player demonstrates {confidence} evidence of significant performance decline",
            _ => $"Player maintains {confidence} stable performance with no clear trend"
        };
    }

    private string GenerateTrendDescription(TrajectoryDirection trend, double rSquared)
    {
        var strength = rSquared > 0.7 ? "strong" : rSquared > 0.4 ? "moderate" : "weak";
        
        return trend switch
        {
            TrajectoryDirection.StronglyImproving => $"Strong improvement trend ({strength} confidence)",
            TrajectoryDirection.Improving => $"Improving trend ({strength} confidence)",
            TrajectoryDirection.Declining => $"Declining trend ({strength} confidence)",
            TrajectoryDirection.StronglyDeclining => $"Strong decline trend ({strength} confidence)",
            _ => $"Stable performance ({strength} confidence)"
        };
    }

    private ActivityLevel DetermineActivityLevel(int roundsLast7Days, int daysSinceLastPlayed)
    {
        if (daysSinceLastPlayed > 7) return ActivityLevel.Inactive;
        if (roundsLast7Days >= 20) return ActivityLevel.VeryActive;
        if (roundsLast7Days >= 10) return ActivityLevel.Active;
        if (roundsLast7Days >= 5) return ActivityLevel.Moderate;
        if (roundsLast7Days >= 1) return ActivityLevel.Light;
        return ActivityLevel.Inactive;
    }

    private static double ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }
}