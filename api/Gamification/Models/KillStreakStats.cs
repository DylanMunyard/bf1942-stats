namespace api.Gamification.Models;

public class KillStreakStats
{
    public int BestSingleRoundStreak { get; set; }
    public string BestStreakMap { get; set; } = "";
    public string BestStreakServer { get; set; } = "";
    public DateTime BestStreakDate { get; set; }
    public List<KillStreak> RecentStreaks { get; set; } = new();
}
