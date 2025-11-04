namespace api.Gamification.Models;

public class KillStreak
{
    public string PlayerName { get; set; } = "";
    public int StreakCount { get; set; }
    public DateTime StreakStart { get; set; }
    public DateTime StreakEnd { get; set; }
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public string RoundId { get; set; } = "";
    public bool IsActive { get; set; }
}
