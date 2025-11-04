namespace api.Gamification.Models;

public class PlayerAchievementSummary
{
    public string PlayerName { get; set; } = "";
    public List<Achievement> RecentAchievements { get; set; } = new();
    public List<Achievement> AllBadges { get; set; } = new();
    public List<Achievement> Milestones { get; set; } = new();
    public List<Achievement> TeamVictories { get; set; } = new();
    public KillStreakStats BestStreaks { get; set; } = new();
    public DateTime LastCalculated { get; set; }
}
