namespace api.DataExplorer.Models;

/// <summary>
/// Represents a single randomized engagement statistic for a server
/// </summary>
public class ServerEngagementStat
{
    /// <summary>
    /// The main statistic value (e.g., "2,341", "89.2%", "15")
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
/// Response containing randomized engagement statistics for a server
/// </summary>
public class ServerEngagementStatsDto
{
    /// <summary>
    /// Array of 3 randomized engagement statistics
    /// </summary>
    public ServerEngagementStat[] Stats { get; set; } = Array.Empty<ServerEngagementStat>();
}