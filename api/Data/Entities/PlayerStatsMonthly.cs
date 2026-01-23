using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed monthly statistics for each player.
/// Recalculated periodically via aggregate query - idempotent by design.
/// For lifetime stats, SUM across all months.
/// </summary>
public class PlayerStatsMonthly
{
    public required string PlayerName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double AvgScorePerRound { get; set; }
    public double KdRatio { get; set; }
    public double KillRate { get; set; }
    public Instant FirstRoundTime { get; set; }
    public Instant LastRoundTime { get; set; }
    public Instant UpdatedAt { get; set; }
}
