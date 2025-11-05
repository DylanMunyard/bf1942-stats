namespace api.Players.Models;

// New model classes for enhanced insights
public class KillMilestone
{
    public int Milestone { get; set; }
    public DateTime AchievedDate { get; set; }
    public int TotalKillsAtMilestone { get; set; }
    public int DaysToAchieve { get; set; }
}
