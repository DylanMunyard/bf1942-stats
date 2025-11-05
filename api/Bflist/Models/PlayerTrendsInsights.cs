namespace api.Bflist.Models;

public class PlayerTrendsInsights
{
    /// <summary>
    /// Overall average players for the entire period
    /// </summary>
    public double OverallAverage { get; set; }

    /// <summary>
    /// 7-day rolling average data points (for periods longer than 7 days)
    /// </summary>
    public RollingAverageDataPoint[] RollingAverage { get; set; } = [];

    /// <summary>
    /// Trend direction: "increasing", "decreasing", "stable"
    /// </summary>
    public string TrendDirection { get; set; } = "";

    /// <summary>
    /// Percentage change from start to end of period
    /// </summary>
    public double PercentageChange { get; set; }

    /// <summary>
    /// Peak player count in the period
    /// </summary>
    public int PeakPlayers { get; set; }

    /// <summary>
    /// Timestamp when peak was reached
    /// </summary>
    public DateTime PeakTimestamp { get; set; }

    /// <summary>
    /// Lowest player count in the period
    /// </summary>
    public int LowestPlayers { get; set; }

    /// <summary>
    /// Timestamp when lowest count was reached
    /// </summary>
    public DateTime LowestTimestamp { get; set; }

    /// <summary>
    /// Explanation of how player counts are calculated for this time period
    /// </summary>
    public string CalculationMethod { get; set; } = "";
}
