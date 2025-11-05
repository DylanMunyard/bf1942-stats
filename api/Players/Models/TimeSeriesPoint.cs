namespace api.Players.Models;

public class TimeSeriesPoint
{
    public DateTime Timestamp { get; set; }
    public double KdRatio { get; set; }
    public double KillRate { get; set; }
}
