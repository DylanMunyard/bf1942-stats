using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Global average statistics per map.
/// Used for calculating player dominance scores in comparison features.
/// </summary>
public class MapGlobalAverage
{
    /// <summary>
    /// Sentinel value for global (cross-server) averages.
    /// </summary>
    public const string GlobalServerGuid = "";

    public required string MapName { get; set; }

    /// <summary>
    /// Server GUID, or empty string for global averages.
    /// Use <see cref="GlobalServerGuid"/> for cross-server aggregates.
    /// </summary>
    public string ServerGuid { get; set; } = GlobalServerGuid;

    public double AvgKillRate { get; set; } // Kills per minute
    public double AvgScoreRate { get; set; } // Score per minute
    public int SampleCount { get; set; }
    public Instant UpdatedAt { get; set; }
}
