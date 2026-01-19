namespace api.Gamification.Models;

public class PlayerAchievementGroup
{
    public string AchievementId { get; set; } = "";
    public string AchievementName { get; set; } = "";
    public string AchievementType { get; set; } = "";
    public string Tier { get; set; } = "";
    public string Game { get; set; } = "";
    public int Count { get; set; }
    public int LatestValue { get; set; }
    public DateTime LatestAchievedAt { get; set; }
}
