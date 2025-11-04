namespace api.Gamification.Models;

/// <summary>
/// Enhanced achievement response that includes player's achievement IDs for filtering
/// </summary>
public class AchievementResponse
{
    public List<Achievement> Items { get; set; } = new List<Achievement>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }

    /// <summary>
    /// Player name if filtering by specific player
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Labeled achievement IDs with their type, tier, and category information
    /// </summary>
    public List<AchievementLabel> PlayerAchievementLabels { get; set; } = new List<AchievementLabel>();
}
