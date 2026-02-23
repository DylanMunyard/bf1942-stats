namespace api.PlayerRelationships.Models;

/// <summary>
/// Squad recommendation for a player.
/// </summary>
public record SquadRecommendation
{
    /// <summary>
    /// Recommended player name.
    /// </summary>
    public required string PlayerName { get; init; }
    
    /// <summary>
    /// Compatibility score (0-100).
    /// </summary>
    public required double CompatibilityScore { get; init; }
    
    /// <summary>
    /// Reasons for the recommendation.
    /// </summary>
    public required List<string> Reasons { get; init; }
    
    /// <summary>
    /// Common servers where both players are active.
    /// </summary>
    public required List<CommonServer> CommonServers { get; init; }
    
    /// <summary>
    /// Typical play times overlap.
    /// </summary>
    public required PlayTimeOverlap PlayTimeOverlap { get; init; }
    
    /// <summary>
    /// Skill level comparison.
    /// </summary>
    public SkillComparison? SkillComparison { get; init; }
    
    /// <summary>
    /// Whether the player is currently online.
    /// </summary>
    public bool IsOnline { get; init; }
    
    /// <summary>
    /// Discord username if available.
    /// </summary>
    public string? DiscordUsername { get; init; }
}

/// <summary>
/// Common server information.
/// </summary>
public record CommonServer
{
    public required string ServerGuid { get; init; }
    public required string ServerName { get; init; }
    public required int BothPlayedSessions { get; init; }
    public required DateTime LastSeenTogether { get; init; }
}

/// <summary>
/// Play time overlap statistics.
/// </summary>
public record PlayTimeOverlap
{
    /// <summary>
    /// Percentage of time both players are online together (0-100).
    /// </summary>
    public required double OverlapPercentage { get; init; }
    
    /// <summary>
    /// Most common overlapping hours (UTC).
    /// </summary>
    public required List<int> CommonHoursUtc { get; init; }
    
    /// <summary>
    /// Most common overlapping days of week.
    /// </summary>
    public required List<DayOfWeek> CommonDays { get; init; }
}

/// <summary>
/// Skill level comparison between two players.
/// </summary>
public record SkillComparison
{
    public required double Player1SkillRating { get; init; }
    public required double Player2SkillRating { get; init; }
    public required double SkillDifference { get; init; }
    public required string SkillMatch { get; init; } // "Perfect", "Good", "Fair", "Poor"
}