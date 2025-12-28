using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed monthly player statistics per map.
/// Recalculated periodically via aggregate query - idempotent by design.
/// For lifetime stats, SUM across all months.
/// </summary>
public class PlayerMapStats
{
    /// <summary>
    /// Sentinel value for global (cross-server) map stats.
    /// </summary>
    public const string GlobalServerGuid = "";

    public required string PlayerName { get; set; }
    public required string MapName { get; set; }

    /// <summary>
    /// Server GUID, or empty string for global map stats.
    /// Use <see cref="GlobalServerGuid"/> for cross-server aggregates.
    /// </summary>
    public string ServerGuid { get; set; } = GlobalServerGuid;

    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public Instant UpdatedAt { get; set; }

    /// <summary>
    /// Returns true if this represents global (cross-server) stats.
    /// </summary>
    public bool IsGlobal => ServerGuid == GlobalServerGuid;
}
