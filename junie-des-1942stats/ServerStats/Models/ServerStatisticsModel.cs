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
    public int HighestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ServerContextInfo
{
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; }
    public string? GameId { get; set; }
    public string? GameName { get; set; }
    public int TotalMinutesPlayed { get; set; }
    public int TotalSessions { get; set; }
    public int TotalPlayers { get; set; }
    public double AveragePlayersPerSession => TotalSessions > 0 ? Math.Round((double)TotalPlayers / TotalSessions, 2) : 0;
    public DateTime LastPlayed { get; set; }
    public bool IsActive => LastPlayed > DateTime.UtcNow.AddDays(-7);
}


public class PagedResult<T>
{
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public IEnumerable<T> Items { get; set; }
    public ServerContextInfo? ServerContext { get; set; }
}