namespace api.ClickHouse.Models;

/// <summary>
/// Kill milestone record for a player
/// </summary>
public class PlayerKillMilestone
{
    public string PlayerName { get; set; } = "";
    public int Milestone { get; set; }
    public DateTime AchievedDate { get; set; }
    public int TotalKillsAtMilestone { get; set; }
    public int DaysToAchieve { get; set; }
}
