using api.Players.Models;

namespace api.ClickHouse.Models;

/// <summary>
/// Result of comparing two players
/// </summary>
public class PlayerComparisonResult
{
    public string Player1 { get; set; } = string.Empty;
    public string Player2 { get; set; } = string.Empty;
    public ServerDetails? ServerDetails { get; set; }
    public List<KillRateComparison> KillRates { get; set; } = new();
    public List<BucketTotalsComparison> BucketTotals { get; set; } = new();
    public List<PingComparison> AveragePing { get; set; } = new();
    public List<MapPerformanceComparison> MapPerformance { get; set; } = new();
    public List<HeadToHeadSession> HeadToHead { get; set; } = new();
    public List<ServerDetails> CommonServers { get; set; } = new();
    public List<KillMilestone> Player1KillMilestones { get; set; } = new();
    public List<KillMilestone> Player2KillMilestones { get; set; } = new();
    public List<MilestoneAchievement> Player1MilestoneAchievements { get; set; } = new();
    public List<MilestoneAchievement> Player2MilestoneAchievements { get; set; } = new();
}

/// <summary>
/// Kill rate comparison between two players
/// </summary>
public class KillRateComparison
{
    public string PlayerName { get; set; } = string.Empty;
    public double KillRate { get; set; }
}

/// <summary>
/// Bucket totals comparison between two players
/// </summary>
public class BucketTotalsComparison
{
    public string Bucket { get; set; } = string.Empty;
    public PlayerTotals Player1Totals { get; set; } = new();
    public PlayerTotals Player2Totals { get; set; } = new();
}

/// <summary>
/// Player totals (kills, deaths, score, play time)
/// </summary>
public class PlayerTotals
{
    public int Score { get; set; }
    public uint Kills { get; set; }
    public uint Deaths { get; set; }
    public double PlayTimeMinutes { get; set; }
}

/// <summary>
/// Ping comparison between two players
/// </summary>
public class PingComparison
{
    public string PlayerName { get; set; } = string.Empty;
    public double AveragePing { get; set; }
}

/// <summary>
/// Map performance comparison between two players
/// </summary>
public class MapPerformanceComparison
{
    public string MapName { get; set; } = string.Empty;
    public PlayerTotals Player1Totals { get; set; } = new();
    public PlayerTotals Player2Totals { get; set; } = new();
}

/// <summary>
/// Head-to-head session between two players
/// </summary>
public class HeadToHeadSession
{
    public DateTime Timestamp { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int Player1Score { get; set; }
    public int Player1Kills { get; set; }
    public int Player1Deaths { get; set; }
    public int Player2Score { get; set; }
    public int Player2Kills { get; set; }
    public int Player2Deaths { get; set; }
    public DateTime Player2Timestamp { get; set; }
    public string? RoundId { get; set; } // For UI linking to round details
}

/// <summary>
/// Server details for player comparison
/// </summary>
public class ServerDetails
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public string GameId { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public string? Org { get; set; }
}

/// <summary>
/// Milestone achievement record
/// </summary>
public class MilestoneAchievement
{
    public string AchievementId { get; set; } = string.Empty;
    public string AchievementName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public uint Value { get; set; }
    public DateTime AchievedAt { get; set; }
}

/// <summary>
/// Result containing similar players to a target player
/// </summary>
public class SimilarPlayersResult
{
    public string TargetPlayer { get; set; } = "";
    public PlayerSimilarityStats? TargetPlayerStats { get; set; }
    public List<SimilarPlayer> SimilarPlayers { get; set; } = new();
}

/// <summary>
/// Player similarity statistics
/// </summary>
public class PlayerSimilarityStats
{
    public string PlayerName { get; set; } = "";
    public uint TotalKills { get; set; }
    public uint TotalDeaths { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double KillDeathRatio { get; set; }
    public double KillsPerMinute => TotalPlayTimeMinutes > 0 ? TotalKills / TotalPlayTimeMinutes : 0;
    public string FavoriteServerName { get; set; } = "";
    public double FavoriteServerPlayTimeMinutes { get; set; }
    public List<string> GameIds { get; set; } = new();
    public double TemporalOverlapMinutes { get; set; }
    public List<int> TypicalOnlineHours { get; set; } = new();
    public Dictionary<string, double> ServerPings { get; set; } = new(); // server_name -> average_ping
    public Dictionary<string, double> MapDominanceScores { get; set; } = new(); // map_name -> dominance_score
    public double TemporalNonOverlapScore { get; set; } // For alias detection: higher = less overlap
}

/// <summary>
/// Similar player record
/// </summary>
public class SimilarPlayer
{
    public string PlayerName { get; set; } = "";
    public uint TotalKills { get; set; }
    public uint TotalDeaths { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double KillDeathRatio { get; set; }
    public double KillsPerMinute { get; set; }
    public string FavoriteServerName { get; set; } = "";
    public double FavoriteServerPlayTimeMinutes { get; set; }
    public List<string> GameIds { get; set; } = new();
    public List<int> TypicalOnlineHours { get; set; } = new();
    public Dictionary<string, double> ServerPings { get; set; } = new(); // server_name -> average_ping
    public Dictionary<string, double> MapDominanceScores { get; set; } = new(); // map_name -> dominance_score
    public double TemporalNonOverlapScore { get; set; } // For alias detection: higher = less overlap
    public double TemporalOverlapMinutes { get; set; } // Actual overlap minutes with target player
    public double SimilarityScore { get; set; }
    public List<string> SimilarityReasons { get; set; } = new();
}

/// <summary>
/// Player activity hours comparison
/// </summary>
public class PlayerActivityHoursComparison
{
    public string Player1 { get; set; } = "";
    public string Player2 { get; set; } = "";
    public List<HourlyActivity> Player1ActivityHours { get; set; } = new();
    public List<HourlyActivity> Player2ActivityHours { get; set; } = new();
}

/// <summary>
/// Similarity algorithm modes for finding similar players
/// </summary>
public enum SimilarityMode
{
    /// <summary>General similarity based on play patterns and skills</summary>
    Default,

    /// <summary>Focused on detecting same player using different aliases</summary>
    AliasDetection
}
