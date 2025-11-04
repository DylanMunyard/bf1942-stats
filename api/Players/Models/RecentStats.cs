namespace api.PlayerStats.Models;

// Time Series Trend Analysis for Player Performance (6-month lookback)
public class RecentStats
{
    public DateTime AnalysisPeriodStart { get; set; }
    public DateTime AnalysisPeriodEnd { get; set; }
    public int TotalRoundsAnalyzed { get; set; }

    // Time series data for K/D ratio and kill rate trends
    public List<TrendDataPoint> KdRatioTrend { get; set; } = new();
    public List<TrendDataPoint> KillRateTrend { get; set; } = new();
}
