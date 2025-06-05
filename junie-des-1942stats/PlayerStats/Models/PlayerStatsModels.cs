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
}

public class PlayerTimeStatistics
{
    public int TotalSessions { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstPlayed { get; set; }
    public DateTime LastPlayed { get; set; }
    public int HighestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }

// New properties
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
    public Session? BestSession { get; set; }
    public List<Session> RecentSessions { get; set; } = [];
    
    public PlayerInsights Insights { get; set; } = new();
}

public class ServerInfo
{
    public string ServerGuid { get; set; }
    public string ServerName { get; set; }
    public int SessionKills { get; set; }
    public int SessionDeaths { get; set; }
    public string MapName { get; set; }
    public string GameId { get; set; }
}

public class Session
{
    public int SessionId { get; set; } // Auto-incremented
    public DateTime StartTime { get; set; }
    public DateTime LastSeenTime { get; set; }
    public bool IsActive { get; set; } // True if session is ongoing
    public int TotalScore { get; set; } // Can track highest score or final score
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public string ServerName { get; set; } = "";
}

public class SessionDetail
{
    public int SessionId { get; set; }
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
}

public class PlayerInsights
{
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }

    // Server rankings
    public List<ServerRanking> ServerRankings { get; set; } = new List<ServerRanking>();

    // Favorite maps by time played
    public List<MapPlayTime> FavoriteMaps { get; set; } = new List<MapPlayTime>();

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
    public int TotalSessions { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}

public class SessionListItem
{
    public int SessionId { get; set; }
    public string ServerName { get; set; }
    public string MapName { get; set; }
    public string GameType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public bool IsActive { get; set; }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }
    public string TeamLabel { get; set; }
}

public class LeaderboardSnapshot
{
    public DateTime Timestamp { get; set; }
    public List<LeaderboardEntry> Entries { get; set; } = new();
}

public class SessionRoundReport
{
    public SessionInfo Session { get; set; } = new();
    public RoundInfo Round { get; set; } = new();
    public List<RoundParticipant> Participants { get; set; } = new();
    public List<LeaderboardSnapshot> LeaderboardSnapshots { get; set; } = new();
}

public class SessionInfo
{
    public int SessionId { get; set; }
    public string PlayerName { get; set; }
    public string ServerName { get; set; }
    public string ServerGuid { get; set; }
    public string GameId { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Score { get; set; }
}

public class RoundInfo
{
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int TotalParticipants { get; set; }
    public bool IsActive { get; set; }
}

public class RoundParticipant
{
    public string PlayerName { get; set; } = "";
    public DateTime JoinTime { get; set; }
    public DateTime LeaveTime { get; set; }
    public int DurationMinutes { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public double KillDeathRatio { get; set; }
    public bool IsActive { get; set; }
}
