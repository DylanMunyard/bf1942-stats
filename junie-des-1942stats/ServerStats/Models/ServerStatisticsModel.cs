using System.Text.Json;
using System.Runtime.Serialization;
using junie_des_1942stats.Gamification.Models;

namespace junie_des_1942stats.ServerStats.Models;

[DataContract(Name = "ServerStatsServerStatistics")]
public class ServerStatistics
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId { get; set; } = "";
    public string Region { get; set; } = "";
    public string Country { get; set; } = "";
    public string Timezone { get; set; } = "";
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; }
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }

    // Recent rounds (typically last 20) with session links
    public List<RoundInfo> RecentRounds { get; set; } = new List<RoundInfo>();

    // Current map being played (null if server has no active players)
    public string? CurrentMap { get; set; }

    // Server busy indicator data
    public ServerBusyIndicator? BusyIndicator { get; set; }
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

public class TopKDRatio
{
    public string PlayerName { get; set; } = "";
    public double KDRatio { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int TotalRounds { get; set; } // Total rounds played in the period
}

public class TopKillRate
{
    public string PlayerName { get; set; } = "";
    public double KillRate { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int PlayTimeMinutes { get; set; }
    public int TotalRounds { get; set; } // Total rounds played in the period
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
    public List<Gamification.Models.Achievement> Achievements { get; set; } = new();
}

public class SessionInfo
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
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
    public string ServerName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int TotalParticipants { get; set; }
    public bool IsActive { get; set; }
    public int? Tickets1 { get; set; }
    public int? Tickets2 { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
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
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
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

// Server busy indicator models (reusing from GameTrendsService but simplified for single server)
public class ServerBusyIndicator
{
    public BusyIndicatorData BusyIndicator { get; set; } = new();
    public List<HourlyBusyData> HourlyTimeline { get; set; } = new(); // 17 hours: 8 before + current + 8 after
    public DateTime GeneratedAt { get; set; }
}

public class BusyIndicatorData
{
    public string BusyLevel { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy, unknown
    public string BusyText { get; set; } = ""; // Human-readable text like "Busier than usual"
    public double CurrentPlayers { get; set; }
    public double TypicalPlayers { get; set; }
    public double Percentile { get; set; } // What percentile the current activity falls into
    public HistoricalRange? HistoricalRange { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public class HourlyBusyData
{
    public int Hour { get; set; } // 0-23 UTC hour
    public double TypicalPlayers { get; set; }
    public string BusyLevel { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy
    public bool IsCurrentHour { get; set; }
}

public class HistoricalRange
{
    public double Min { get; set; }
    public double Q25 { get; set; }
    public double Median { get; set; }
    public double Q75 { get; set; }
    public double Q90 { get; set; }
    public double Max { get; set; }
    public double Average { get; set; }
}

// Server leaderboards for a specific time period
[DataContract(Name = "ServerStatsServerLeaderboards")]
public class ServerLeaderboards
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public int Days { get; set; } // Number of days of data included
    public DateTime StartPeriod { get; set; }
    public DateTime EndPeriod { get; set; }

    // Most active players by time played
    public List<PlayerActivity> MostActivePlayersByTime { get; set; } = new List<PlayerActivity>();

    // Top 10 best scores in the period
    public List<TopScore> TopScores { get; set; } = new List<TopScore>();

    // Top 10 K/D ratios
    public List<TopKDRatio> TopKDRatios { get; set; } = new List<TopKDRatio>();

    // Top 10 kill rates
    public List<TopKillRate> TopKillRates { get; set; } = new List<TopKillRate>();

    // Top 10 placement leaderboard
    public List<PlacementLeaderboardEntry> TopPlacements { get; set; } = new List<PlacementLeaderboardEntry>();

    // Weighted placement leaderboard (only populated when minPlayersForWeighting is specified)
    public List<PlacementLeaderboardEntry>? WeightedTopPlacements { get; set; } = null;
    public int? MinPlayersForWeighting { get; set; } // The minimum player count used for weighting
}