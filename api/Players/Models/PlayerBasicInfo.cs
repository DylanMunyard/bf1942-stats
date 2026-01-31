namespace api.Players.Models;

public class PlayerBasicInfo
{
    public string PlayerName { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }

    // Aggregate stats from PlayerServerStats table
    public int? TotalKills { get; set; }
    public int? TotalDeaths { get; set; }
    public int? TotalRounds { get; set; }
    public string? FavoriteServer { get; set; }

    // Recent activity summary
    public RecentActivitySummary? RecentActivity { get; set; }
}

public class RecentActivitySummary
{
    public int RoundsThisWeek { get; set; }
    public int? LastScore { get; set; }
}
