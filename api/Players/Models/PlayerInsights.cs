namespace api.Players.Models;

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
