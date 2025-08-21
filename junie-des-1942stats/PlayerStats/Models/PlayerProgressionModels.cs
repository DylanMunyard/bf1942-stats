namespace junie_des_1942stats.PlayerStats.Models;

/// <summary>
/// V2 Player Details API models focused on progression and delta analysis
/// </summary>
public class PlayerProgressionDetails
{
    public string PlayerName { get; set; } = "";
    public DateTime AnalysisPeriodStart { get; set; }
    public DateTime AnalysisPeriodEnd { get; set; }
    
    // Core progression metrics
    public OverallProgression OverallProgression { get; set; } = new();
    public List<MapProgression> MapProgressions { get; set; } = new();
    public List<ServerRankingProgression> ServerRankings { get; set; } = new();
    public PerformanceTrajectory PerformanceTrajectory { get; set; } = new();
    public RecentActivity RecentActivity { get; set; } = new();
    
    // Comparative analysis
    public ComparativeMetrics ComparativeMetrics { get; set; } = new();
}

public class OverallProgression
{
    // Current metrics
    public double CurrentKillRate { get; set; }
    public double CurrentKDRatio { get; set; }
    public double CurrentScorePerMinute { get; set; }
    
    // Historical comparison (last 30 days vs previous 30 days)
    public ProgressionDelta KillRateDelta { get; set; } = new();
    public ProgressionDelta KDRatioDelta { get; set; } = new();
    public ProgressionDelta ScorePerMinuteDelta { get; set; } = new();
    
    // Milestone progress
    public List<MilestoneProgress> ActiveMilestones { get; set; } = new();
    public List<RecentAchievement> RecentAchievements { get; set; } = new();
}

public class ProgressionDelta
{
    public double CurrentValue { get; set; }
    public double PreviousValue { get; set; }
    public double AbsoluteChange => CurrentValue - PreviousValue;
    public double PercentageChange => PreviousValue > 0 ? (AbsoluteChange / PreviousValue) * 100 : 0;
    public ProgressionDirection Direction => AbsoluteChange > 0 ? ProgressionDirection.Improving : 
                                           AbsoluteChange < 0 ? ProgressionDirection.Declining : 
                                           ProgressionDirection.Stable;
    public string ChangeDescription => Direction switch
    {
        ProgressionDirection.Improving => $"+{Math.Abs(PercentageChange):F1}% improvement",
        ProgressionDirection.Declining => $"-{Math.Abs(PercentageChange):F1}% decline", 
        _ => "No significant change"
    };
}

public enum ProgressionDirection
{
    Improving,
    Declining, 
    Stable
}

public class MapProgression
{
    public string MapName { get; set; } = "";
    public int TotalRoundsPlayed { get; set; }
    public double TotalPlayTimeHours { get; set; }
    
    // Current performance
    public double CurrentKillRate { get; set; }
    public double CurrentKDRatio { get; set; }
    public double CurrentWinRate { get; set; }
    
    // Progression deltas
    public ProgressionDelta KillRateDelta { get; set; } = new();
    public ProgressionDelta KDRatioDelta { get; set; } = new();
    public ProgressionDelta WinRateDelta { get; set; } = new();
    
    // Comparative metrics
    public double MapAverageKillRate { get; set; }
    public double MapAverageKDRatio { get; set; }
    public PlayerPerformanceRating PerformanceVsAverage { get; set; }
    
    // Trend analysis
    public List<PerformanceDataPoint> PerformanceTrend { get; set; } = new();
}

public enum PlayerPerformanceRating
{
    Exceptional,  // Top 5%
    AboveAverage, // Top 25%
    Average,      // Middle 50%
    BelowAverage, // Bottom 25%
    Poor          // Bottom 5%
}

public class ServerRankingProgression
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId { get; set; } = "";
    
    // Current ranking
    public int CurrentRank { get; set; }
    public int TotalPlayers { get; set; }
    public int CurrentScore { get; set; }
    
    // Ranking progression
    public RankingDelta MonthlyRankingChange { get; set; } = new();
    public List<RankingHistoryPoint> RankingHistory { get; set; } = new();
    
    // Performance insights
    public double PlayTimeHours { get; set; }
    public double AverageSessionScore { get; set; }
    public ProgressionDelta SessionScoreDelta { get; set; } = new();
}

public class RankingDelta
{
    public int PreviousRank { get; set; }
    public int CurrentRank { get; set; }
    public int RankChange => PreviousRank - CurrentRank; // Positive = improved ranking
    public RankingDirection Direction => RankChange > 0 ? RankingDirection.Improved : 
                                       RankChange < 0 ? RankingDirection.Dropped : 
                                       RankingDirection.Maintained;
    public string ChangeDescription => Direction switch
    {
        RankingDirection.Improved => $"Improved by {Math.Abs(RankChange)} positions",
        RankingDirection.Dropped => $"Dropped by {Math.Abs(RankChange)} positions",
        _ => "Maintained position"
    };
}

public enum RankingDirection
{
    Improved,
    Dropped,
    Maintained
}

public class RankingHistoryPoint
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Rank { get; set; }
    public int TotalScore { get; set; }
    public string MonthYear => $"{Year}-{Month:D2}";
}

public class PerformanceTrajectory
{
    public TrajectoryDirection OverallTrajectory { get; set; }
    public double TrajectoryConfidence { get; set; } // 0-1 confidence score
    public string TrajectoryDescription { get; set; } = "";
    
    // Time series data for the last 90 days
    public List<PerformanceDataPoint> KillRateTrajectory { get; set; } = new();
    public List<PerformanceDataPoint> KDRatioTrajectory { get; set; } = new();
    public List<PerformanceDataPoint> ScoreTrajectory { get; set; } = new();
    
    // Trend analysis
    public TrendAnalysis KillRateTrend { get; set; } = new();
    public TrendAnalysis KDRatioTrend { get; set; } = new();
    public TrendAnalysis ScoreTrend { get; set; } = new();
}

public enum TrajectoryDirection
{
    StronglyImproving,
    Improving,
    Stable,
    Declining,
    StronglyDeclining
}

public class PerformanceDataPoint
{
    public DateTime Date { get; set; }
    public double Value { get; set; }
    public int SampleSize { get; set; } // Number of games/rounds
}

public class TrendAnalysis
{
    public double Slope { get; set; } // Linear regression slope
    public double RSquared { get; set; } // Trend strength (0-1)
    public TrajectoryDirection Trend { get; set; }
    public string TrendDescription { get; set; } = "";
}

public class RecentActivity
{
    public DateTime LastPlayedDate { get; set; }
    public int DaysSinceLastPlayed { get; set; }
    public ActivityLevel RecentActivityLevel { get; set; }
    
    // Last 7 days summary
    public int RoundsLast7Days { get; set; }
    public double PlayTimeLast7Days { get; set; }
    public List<string> ServersPlayedLast7Days { get; set; } = new();
    public List<string> MapsPlayedLast7Days { get; set; } = new();
    
    // Activity patterns
    public List<DailyActivity> Last30DaysActivity { get; set; } = new();
    public List<int> PreferredPlayingHours { get; set; } = new(); // Hours of day (0-23)
}

public enum ActivityLevel
{
    VeryActive,   // Daily play
    Active,       // 4-6 days per week
    Moderate,     // 2-3 days per week
    Light,        // 1-2 days per week
    Inactive      // Less than weekly
}

public class DailyActivity
{
    public DateTime Date { get; set; }
    public int RoundsPlayed { get; set; }
    public double PlayTimeMinutes { get; set; }
    public bool WasActive => RoundsPlayed > 0;
}

public class ComparativeMetrics
{
    // Player vs server averages
    public List<ServerComparison> ServerComparisons { get; set; } = new();
    
    // Player vs global averages
    public GlobalComparison GlobalComparison { get; set; } = new();
    
    // Peer group comparison (similar skill players)
    public PeerGroupComparison PeerGroupComparison { get; set; } = new();
}

public class ServerComparison
{
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    
    public double PlayerKillRate { get; set; }
    public double ServerAverageKillRate { get; set; }
    public double PlayerKDRatio { get; set; }
    public double ServerAverageKDRatio { get; set; }
    
    public PlayerPerformanceRating KillRateRating { get; set; }
    public PlayerPerformanceRating KDRatioRating { get; set; }
    
    public int PlayerRank { get; set; }
    public int TotalPlayers { get; set; }
    public double PercentileRank { get; set; } // 0-100
}

public class GlobalComparison
{
    public double GlobalAverageKillRate { get; set; }
    public double GlobalAverageKDRatio { get; set; }
    public double GlobalAverageScorePerMinute { get; set; }
    
    public PlayerPerformanceRating KillRateRating { get; set; }
    public PlayerPerformanceRating KDRatioRating { get; set; }
    public PlayerPerformanceRating ScoreRating { get; set; }
    
    public int GlobalRank { get; set; }
    public int TotalPlayers { get; set; }
    public double GlobalPercentile { get; set; }
}

public class PeerGroupComparison
{
    public string PeerGroupDefinition { get; set; } = "";
    public int PeerGroupSize { get; set; }
    
    public double PeerAverageKillRate { get; set; }
    public double PeerAverageKDRatio { get; set; }
    
    public PlayerPerformanceRating RelativeToPeers { get; set; }
    public int RankInPeerGroup { get; set; }
    
    public List<string> SimilarPlayers { get; set; } = new();
}

public class MilestoneProgress
{
    public string MilestoneType { get; set; } = ""; // "kills", "score", "playtime"
    public string MilestoneName { get; set; } = "";
    public int TargetValue { get; set; }
    public int CurrentValue { get; set; }
    public int RemainingValue => Math.Max(0, TargetValue - CurrentValue);
    public double ProgressPercentage => TargetValue > 0 ? Math.Min(100, (double)CurrentValue / TargetValue * 100) : 0;
    public DateTime? EstimatedCompletionDate { get; set; }
    public string ProgressDescription { get; set; } = "";
}

public class RecentAchievement
{
    public string AchievementName { get; set; } = "";
    public string AchievementDescription { get; set; } = "";
    public DateTime AchievedDate { get; set; }
    public int DaysAgo => (DateTime.UtcNow - AchievedDate).Days;
    public string BadgeType { get; set; } = "";
    public int Value { get; set; }
}