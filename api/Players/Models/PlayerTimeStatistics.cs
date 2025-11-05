namespace api.Players.Models;

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
