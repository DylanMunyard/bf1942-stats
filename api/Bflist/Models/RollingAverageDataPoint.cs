namespace api.Bflist.Models;

public class RollingAverageDataPoint
{
    /// <summary>
    /// Timestamp of the rolling average data point
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 7-day rolling average of players online
    /// </summary>
    public double Average { get; set; }
}
