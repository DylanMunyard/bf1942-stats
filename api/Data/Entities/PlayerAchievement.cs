using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Player achievement entity for SQLite storage.
/// Stores gamification achievements, badges, milestones, and placements.
/// </summary>
public class PlayerAchievement
{
    public long Id { get; set; }
    public string PlayerName { get; set; } = "";
    public string AchievementType { get; set; } = "";
    public string AchievementId { get; set; } = "";
    public string AchievementName { get; set; } = "";
    public string Tier { get; set; } = "";
    public int Value { get; set; }
    public Instant AchievedAt { get; set; }
    public Instant ProcessedAt { get; set; }
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string? Metadata { get; set; }
    public string Game { get; set; } = "";
    public Instant Version { get; set; }
}
