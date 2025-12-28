using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed percentile data for server busy indicators.
/// Used by GetServerBusyIndicatorAsync to show Google-style "how busy" indicators.
/// </summary>
public class ServerHourlyPattern
{
    public required string ServerGuid { get; set; }
    public int DayOfWeek { get; set; } // 0=Sunday, 6=Saturday (SQLite convention)
    public int HourOfDay { get; set; } // 0-23
    public double AvgPlayers { get; set; }
    public double MinPlayers { get; set; }
    public double Q25Players { get; set; }
    public double MedianPlayers { get; set; }
    public double Q75Players { get; set; }
    public double Q90Players { get; set; }
    public double MaxPlayers { get; set; }
    public int DataPoints { get; set; }
    public Instant UpdatedAt { get; set; }
}
