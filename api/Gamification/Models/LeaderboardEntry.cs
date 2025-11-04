namespace api.Gamification.Models;

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int Value { get; set; }
    public string DisplayValue { get; set; } = "";
    public int AchievementCount { get; set; }
    public List<string> TopBadges { get; set; } = new();
}
