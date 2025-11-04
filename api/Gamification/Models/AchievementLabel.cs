namespace api.Gamification.Models;

/// <summary>
/// Achievement label with type, tier, and category information
/// </summary>
public class AchievementLabel
{
    public string AchievementId { get; set; } = "";
    public string AchievementType { get; set; } = "";
    public string Tier { get; set; } = "";
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
