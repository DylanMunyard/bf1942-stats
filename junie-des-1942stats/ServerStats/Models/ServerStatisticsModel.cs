using System.Text.Json;
using System.Runtime.Serialization;

namespace junie_des_1942stats.ServerStats.Models;

[DataContract(Name = "ServerStatsServerStatistics")]
public class ServerStatistics
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId {get;set;} = "";
    public string Region { get; set; } = "";
    public string Country { get; set; } = "";
    public string Timezone { get; set; } = "";
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; }
    
    // Most active players by time played (1 week)
    public List<PlayerActivity> MostActivePlayersByTimeWeek { get; set; } = new List<PlayerActivity>();
    
    // Top 10 best scores in the period (1 week)
    public List<TopScore> TopScoresWeek { get; set; } = new List<TopScore>();
    
    // Most active players by time played (1 month)
    public List<PlayerActivity> MostActivePlayersByTimeMonth { get; set; } = new List<PlayerActivity>();
    
    // Top 10 best scores in the period (1 month)
    public List<TopScore> TopScoresMonth { get; set; } = new List<TopScore>();
    
    // Last 5 rounds with session links
    public List<RoundInfo> LastRounds { get; set; } = new List<RoundInfo>();
    
    // Current map being played (null if server has no active players)
    public string? CurrentMap { get; set; }
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
    public IEnumerable<T> Items { get; set; } = new List<T>();
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
    public string PlayerName { get; set; } = null!;
    public string ServerName { get; set; } = null!;
    public string ServerGuid { get; set; } = null!;
    public string GameId { get; set; } = null!;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Score { get; set; }
    public string? ServerIp { get; set; }
    public int? ServerPort { get; set; }
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

// Server search models
public class ServerBasicInfo
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId { get; set; } = "";
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public int TotalActivePlayersLast24h { get; set; }
    public int TotalPlayersAllTime { get; set; }
    public string? CurrentMap { get; set; }
    public bool HasActivePlayers { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class ServerFilters
{
    public string? ServerName { get; set; }
    public string? GameId { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public bool? HasActivePlayers { get; set; }
    public DateTime? LastActivityFrom { get; set; }
    public DateTime? LastActivityTo { get; set; }
    public int? MinTotalPlayers { get; set; }
    public int? MaxTotalPlayers { get; set; }
    public int? MinActivePlayersLast24h { get; set; }
    public int? MaxActivePlayersLast24h { get; set; }
}