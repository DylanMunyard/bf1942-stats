namespace api.Gamification.Models;

public class GamificationLeaderboard
{
    public string Category { get; set; } = "";
    public string Period { get; set; } = "";
    public List<LeaderboardEntry> Entries { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}
