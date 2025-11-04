namespace api.Gamification.Models;

public class Achievement
{
    public string PlayerName { get; set; } = "";
    public string AchievementType { get; set; } = ""; // 'kill_streak', 'badge', 'milestone'
    public string AchievementId { get; set; } = ""; // 'kill_streak_15', 'sharpshooter_gold'
    public string AchievementName { get; set; } = "";
    public string Tier { get; set; } = ""; // 'bronze', 'silver', 'gold', 'legend'
    public uint Value { get; set; }
    public DateTime AchievedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string Metadata { get; set; } = ""; // JSON for additional context
    public string Game { get; set; } = ""; // Game type: bf1942, fh2, bfvietnam
    public DateTime Version { get; set; } // Version field for ReplacingMergeTree deduplication
}
