namespace junie_des_1942stats.PlayerStats.Models;

public class PlayerBasicInfo
{
    public string PlayerName { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}

public class PlayerFilters
{
    public string? PlayerName { get; set; }
    public int? MinPlayTime { get; set; }
    public int? MaxPlayTime { get; set; }
    public DateTime? LastSeenFrom { get; set; }
    public DateTime? LastSeenTo { get; set; }
    public bool? IsActive { get; set; }
    public string? ServerName { get; set; }
    public string? GameId { get; set; }
    public string? MapName { get; set; }

    // Additional session-specific filters
    public string? ServerGuid { get; set; }
    public string? GameType { get; set; }
    public DateTime? StartTimeFrom { get; set; }
    public DateTime? StartTimeTo { get; set; }
    public int? MinScore { get; set; }
    public int? MaxScore { get; set; }
    public int? MinKills { get; set; }
    public int? MaxKills { get; set; }
    public int? MinDeaths { get; set; }
    public int? MaxDeaths { get; set; }
}

public class PlayerTimeStatistics
{
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstPlayed { get; set; }
    public DateTime LastPlayed { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }

    // New properties
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
    public List<Session> RecentSessions { get; set; } = [];

    public PlayerInsights Insights { get; set; } = new();

    // Server-specific insights (replaces BestScores)
    public List<ServerInsight> Servers { get; set; } = new();

    // Recent performance stats from last 60 sessions
    public RecentStats? RecentStats { get; set; }

    // Best scores for different time periods
    public PlayerBestScores? BestScores { get; set; }
}

public class ServerInfo
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int SessionKills { get; set; }
    public int SessionDeaths { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
}

public class Session
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string? GameType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastSeenTime { get; set; }
    public bool IsActive { get; set; } // True if session is ongoing
    public int TotalScore { get; set; } // Can track highest score or final score
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public string GameId { get; set; } = string.Empty;
}

public class SessionDetail
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string PlayerName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public bool IsActive { get; set; }

    // Related entity details
    public PlayerDetailInfo PlayerDetails { get; set; } = new();
    public ServerDetailInfo? ServerDetails { get; set; }
    public List<ObservationInfo> Observations { get; set; } = new();
}

public class PlayerDetailInfo
{
    public string Name { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsAiBot { get; set; }
}

public class ServerDetailInfo
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public int MaxPlayers { get; set; }
    public string GameId { get; set; } = "";
}

public class ObservationInfo
{
    public DateTime Timestamp { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }
    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";
}

public class ObservationBucket
{
    public DateTime Timestamp { get; set; }
    public string PlayerName { get; set; } = "";
    public List<ObservationInfo> Observations { get; set; } = new();
}

public class ServerRanking
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int TotalScore { get; set; }
    public int TotalRankedPlayers { get; set; }
    public string RankDisplay => $"{Rank} of {TotalRankedPlayers}";
    public string ScoreDisplay => $"{TotalScore} points";

    public double AveragePing { get; set; }
    public List<MonthlyServerRanking> HistoricalRankings { get; set; } = new();
}

public class PlayerInsights
{
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }

    // Server rankings
    public List<ServerRanking> ServerRankings { get; set; } = new List<ServerRanking>();

    // Hours when the player is typically online
    public List<HourlyActivity> ActivityByHour { get; set; } = new List<HourlyActivity>();
}

public class ServerPlayTime
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int MinutesPlayed { get; set; }
}

public class MapPlayTime
{
    public string MapName { get; set; } = string.Empty;
    public int MinutesPlayed { get; set; }
    public double KDRatio { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalKills { get; set; }
}

public class MapKillStats
{
    public string MapName { get; set; } = string.Empty;
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KdRatio => TotalDeaths > 0 ? Math.Round((double)TotalKills / TotalDeaths, 2) : TotalKills;
}

public class HourlyActivity
{
    public int Hour { get; set; }
    public int MinutesActive { get; set; }
    public string FormattedHour => $"{Hour:D2}:00 - {Hour:D2}:59";
}

public class ServerBestScore
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int BestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int PlayTimeMinutes { get; set; }
    public DateTime BestScoreDate { get; set; }
    public string MapName { get; set; } = string.Empty;
    public int SessionId { get; set; }
}

public class ServerBestScoreRaw
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int BestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int PlayTimeMinutes { get; set; }
    public DateTime BestScoreDate { get; set; }
    public string MapName { get; set; } = string.Empty;
    public int SessionId { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new List<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }

    // Add player context information
    public PlayerContextInfo? PlayerInfo { get; set; }
}

// New class to hold player context information
public class PlayerContextInfo
{
    public string Name { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}

public class SessionListItem
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public bool IsActive { get; set; }
}

// New model classes for enhanced insights
public class KillMilestone
{
    public int Milestone { get; set; }
    public DateTime AchievedDate { get; set; }
    public int TotalKillsAtMilestone { get; set; }
    public int DaysToAchieve { get; set; }
}

public class ServerInsight
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId { get; set; } = "";
    public double TotalMinutes { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int HighestScore { get; set; }
    public string HighestScoreSessionId { get; set; } = "";
    public string HighestScoreMapName { get; set; } = "";
    public DateTime HighestScoreStartTime { get; set; }
    public double KillsPerMinute { get; set; }
    public int TotalRounds { get; set; }
    public ServerRanking? Ranking { get; set; }
    public double KdRatio => TotalDeaths > 0 ? Math.Round((double)TotalKills / TotalDeaths, 2) : TotalKills;
}

public class MonthlyServerRanking
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Rank { get; set; }
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public string MonthYearDisplay => $"{Year}-{Month:D2}";
}

// Time Series Trend Analysis for Player Performance (6-month lookback)
public class RecentStats
{
    public DateTime AnalysisPeriodStart { get; set; }
    public DateTime AnalysisPeriodEnd { get; set; }
    public int TotalRoundsAnalyzed { get; set; }

    // Time series data for K/D ratio and kill rate trends
    public List<TrendDataPoint> KdRatioTrend { get; set; } = new();
    public List<TrendDataPoint> KillRateTrend { get; set; } = new();
}

public class TrendDataPoint
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
}

// Best scores for different time periods
public class PlayerBestScores
{
    public List<BestScoreDetail> ThisWeek { get; set; } = new();
    public List<BestScoreDetail> Last30Days { get; set; } = new();
    public List<BestScoreDetail> AllTime { get; set; } = new();
}

public class BestScoreDetail
{
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string RoundId { get; set; } = string.Empty;
}


