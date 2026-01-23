namespace api.Analytics.Models;

/// <summary>
/// Current activity status across games and servers.
/// </summary>
public class CurrentActivityStatus
{
    public string Game { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public DateTime LatestActivity { get; set; }
    public string CurrentMapName { get; set; } = "";
}

/// <summary>
/// Weekly activity patterns to identify weekend vs weekday differences.
/// </summary>
public class WeeklyActivityPattern
{
    public int DayOfWeek { get; set; }
    public int HourOfDay { get; set; }
    public int UniquePlayers { get; set; }
    public int TotalRounds { get; set; }
    public double AvgRoundDuration { get; set; }
    public string PeriodType { get; set; } = ""; // Weekend/Weekday
}

/// <summary>
/// Trend insights for server activity.
/// </summary>
public class TrendInsights
{
    public double CurrentHourAvgPlayers { get; set; }
    public double CurrentHourAvgRounds { get; set; }
    public double NextHourAvgPlayers { get; set; }
    public double NextHourAvgRounds { get; set; }
    public string TrendDirection { get; set; } = ""; // increasing/decreasing
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Activity metrics for current state.
/// </summary>
public class ActivityMetrics
{
    public double AvgCurrentPlayers { get; set; }
    public double AvgCurrentRounds { get; set; }
}

/// <summary>
/// Smart prediction insights combining real-time and historical data.
/// </summary>
public class SmartPredictionInsights
{
    public double CurrentHourPredictedPlayers { get; set; }
    public int CurrentActualPlayers { get; set; }
    public string ActivityComparisonStatus { get; set; } = ""; // busier_than_usual, quieter_than_usual, as_usual
    public string CurrentStatus { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy
    public string TrendDirection { get; set; } = ""; // increasing_significantly, increasing, stable, decreasing, decreasing_significantly
    public double NextHourPredictedPlayers { get; set; }
    public double MaxPredictedPlayers { get; set; }
    public List<HourlyPrediction> Forecast { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Hourly prediction data point.
/// </summary>
public class HourlyPrediction
{
    public int HourOfDay { get; set; }
    public int DayOfWeek { get; set; }
    public double PredictedPlayers { get; set; }
    public int DataPoints { get; set; }
    public bool IsCurrentHour { get; set; }
    public int? ActualPlayers { get; set; } // Only set for current hour
    public double? Delta { get; set; } // Actual - Predicted, only for current hour
}

/// <summary>
/// Peak prediction for a 24-hour period.
/// </summary>
public class Peak24HourPrediction
{
    public int HourOfDay { get; set; }
    public int DayOfWeek { get; set; }
    public double PredictedPlayers { get; set; }
}

/// <summary>
/// Data structure for querying hourly timeline data.
/// </summary>
internal class HourlyTimelineData
{
    public int Hour { get; set; }
    public double AvgPlayers { get; set; }
}

/// <summary>
/// Hourly timeline data per server.
/// </summary>
internal class ServerHourlyTimelineData
{
    public string ServerGuid { get; set; } = "";
    public int Hour { get; set; }
    public double AvgPlayers { get; set; }
}

/// <summary>
/// Represents hourly busyness data for timeline visualization.
/// </summary>
public class HourlyBusyData
{
    public int Hour { get; set; } // 0-23 UTC hour
    public double TypicalPlayers { get; set; }
    public string BusyLevel { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy
    public bool IsCurrentHour { get; set; }
}

/// <summary>
/// Busy indicator result for a specific server.
/// </summary>
public class BusyIndicatorResult
{
    public string BusyLevel { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy, unknown
    public string BusyText { get; set; } = ""; // Human-readable text like "Busier than usual"
    public double CurrentPlayers { get; set; }
    public double TypicalPlayers { get; set; }
    public double Percentile { get; set; } // What percentile the current activity falls into
    public HistoricalRange? HistoricalRange { get; set; }
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Busy indicator result for a single server.
/// </summary>
public class ServerBusyIndicatorResult
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Game { get; set; } = "";
    public BusyIndicatorResult BusyIndicator { get; set; } = new();
    public List<HourlyBusyData> HourlyTimeline { get; set; } = new();
}

/// <summary>
/// Grouped result containing busy indicators for multiple servers.
/// </summary>
public class GroupedServerBusyIndicatorResult
{
    public List<ServerBusyIndicatorResult> ServerResults { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Server information for game trends.
/// </summary>
public class GameTrendsServerInfo
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Game { get; set; } = "";
}

/// <summary>
/// Historical range statistics.
/// </summary>
public class HistoricalRange
{
    public double Min { get; set; }
    public double Q25 { get; set; }
    public double Median { get; set; }
    public double Q75 { get; set; }
    public double Q90 { get; set; }
    public double Max { get; set; }
    public double Average { get; set; }
}

/// <summary>
/// Current busy metrics.
/// </summary>
public class CurrentBusyMetrics
{
    public double CurrentPlayers { get; set; }
}

/// <summary>
/// Historical busyness statistics.
/// </summary>
public class HistoricalBusyStats
{
    public double AvgPlayers { get; set; }
    public double Q25Players { get; set; }
    public double MedianPlayers { get; set; }
    public double Q75Players { get; set; }
    public double Q90Players { get; set; }
    public double MaxPlayers { get; set; }
    public double MinPlayers { get; set; }
    public int DataPoints { get; set; }
}

/// <summary>
/// Daily averages result.
/// </summary>
public class DailyAveragesResult
{
    public double[] DailyAverages { get; set; } = Array.Empty<double>();
}

/// <summary>
/// Current activity per server.
/// </summary>
public class ServerCurrentActivity
{
    public string ServerGuid { get; set; } = "";
    public double CurrentPlayers { get; set; }
}

/// <summary>
/// Historical data per server.
/// </summary>
public class ServerHistoricalData
{
    public string ServerGuid { get; set; } = "";
    public double[] DailyAverages { get; set; } = Array.Empty<double>();
}
