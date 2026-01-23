using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed weekly player statistics per server.
/// Recalculated periodically via aggregate query - idempotent by design.
/// For different time periods, SUM across weeks:
/// - Weekly: current week only
/// - Monthly: last 4-5 weeks
/// - All-time: SUM all records
/// </summary>
public class PlayerServerStats
{
    public required string PlayerName { get; set; }
    public required string ServerGuid { get; set; }
    public int Year { get; set; }
    public int Week { get; set; } // ISO week 1-53
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public Instant UpdatedAt { get; set; }
}
