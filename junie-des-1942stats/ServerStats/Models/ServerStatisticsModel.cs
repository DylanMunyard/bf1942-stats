using System.Text.Json;
using junie_des_1942stats.Prometheus;

namespace junie_des_1942stats.ServerStats.Models;

public class ServerStatistics
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }
    
    // Most active players by time played
    public List<PlayerActivity> MostActivePlayersByTime { get; set; } = new List<PlayerActivity>();
    
    // Top 10 best scores in the period
    public List<TopScore> TopScores { get; set; } = new List<TopScore>();
    
    // Player count metrics
    public List<PrometheusService.TimeSeriesPoint> PlayerCountMetrics { get; set; } = [];
    
    // Last 5 rounds with session links
    public List<RoundInfo> LastRounds { get; set; } = new List<RoundInfo>();
}

public class PlayerActivity
{
    public string PlayerName { get; set; } = "";
    public int MinutesPlayed { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KdRatio => TotalDeaths > 0 ? Math.Round((double)TotalKills / TotalDeaths, 2) : TotalKills;
}

public class TopScore
{
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public string MapName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int SessionId { get; set; }
}

public class MapStatistics
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }
    
    // Map statistics
    public int PlayerCount { get; set; }
    public int TotalMinutesPlayed { get; set; }
    public int TotalSessions { get; set; }
    
    // Most active players by time played on this map
    public List<PlayerActivity> MostActivePlayersByTime { get; set; } = new List<PlayerActivity>();
    
    // Top scores on this map
    public List<TopScore> TopScores { get; set; } = new List<TopScore>();
}

public class ServerRanking
{
    public int Rank { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int TotalScore { get; set; } // Changed from HighestScore
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}

public class ServerContextInfo
{
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public int TotalMinutesPlayed { get; set; }
    public int TotalPlayers { get; set; }
}

public class PagedResult<T>
{
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public IEnumerable<T> Items { get; set; }
    public ServerContextInfo? ServerContext { get; set; }
}

// Round report models
public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }
    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";
}

public class LeaderboardSnapshot
{
    public DateTime Timestamp { get; set; }
    public List<LeaderboardEntry> Entries { get; set; } = new();
}

public class SessionRoundReport
{
    public SessionInfo Session { get; set; } = new();
    public RoundReportInfo Round { get; set; } = new();
    public List<RoundParticipant> Participants { get; set; } = new();
    public List<LeaderboardSnapshot> LeaderboardSnapshots { get; set; } = new();
}

public class SessionInfo
{
    public int SessionId { get; set; }
    public string PlayerName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string GameId { get; set; } = "";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Score { get; set; }
}

public class RoundReportInfo
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