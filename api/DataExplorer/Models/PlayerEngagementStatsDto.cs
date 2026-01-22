namespace api.DataExplorer.Models;

/// <summary>
/// Represents a single randomized engagement statistic for a player
/// </summary>
public class PlayerEngagementStat
{
    /// <summary>
    /// The main statistic value (e.g., "47,231", "89.2%", "15")
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// The label describing what this statistic represents
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Optional context or additional information
    /// </summary>
    public string? Context { get; set; }
}

/// <summary>
/// Response containing randomized engagement statistics for a player
/// </summary>
public class PlayerEngagementStatsDto
{
    /// <summary>
    /// Array of 3 randomized engagement statistics
    /// </summary>
    public PlayerEngagementStat[] Stats { get; set; } = Array.Empty<PlayerEngagementStat>();
}