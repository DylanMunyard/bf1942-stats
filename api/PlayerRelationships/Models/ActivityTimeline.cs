namespace api.PlayerRelationships.Models;

/// <summary>
/// Timeline of player activity showing when accounts were active/dormant.
/// Useful for visualizing account switchover patterns.
/// </summary>
public class ActivityTimeline
{
    /// <summary>
    /// Player 1 activity window.
    /// </summary>
    public required ActivityPeriod Player1Activity { get; set; }

    /// <summary>
    /// Player 2 activity window.
    /// </summary>
    public required ActivityPeriod Player2Activity { get; set; }

    /// <summary>
    /// Gap analysis between the two accounts.
    /// </summary>
    public required GapAnalysis Gap { get; set; }

    /// <summary>
    /// Daily activity for visualization (last 180 days).
    /// </summary>
    public required List<DailyActivity> Player1Timeline { get; set; }
    public required List<DailyActivity> Player2Timeline { get; set; }

    /// <summary>
    /// ASCII-art timeline for quick visual inspection.
    /// </summary>
    public required string AsciiTimeline { get; set; }

    /// <summary>
    /// Analysis of the timeline pattern.
    /// </summary>
    public required string Analysis { get; set; }

    /// <summary>
    /// How suspicious is this switchover pattern? 0-1.
    /// </summary>
    public double SwitchoverSuspicionScore { get; set; }

    /// <summary>
    /// Whether switchover analysis has sufficient data to be meaningful.
    /// Large gaps (>30 days) are treated as dormancy, not active switching.
    /// </summary>
    public bool HasSufficientData { get; set; } = true;
}

/// <summary>
/// Activity period for a player account.
/// </summary>
public class ActivityPeriod
{
    /// <summary>
    /// First time this player was seen.
    /// </summary>
    public DateTime FirstSeen { get; set; }

    /// <summary>
    /// Last time this player was seen.
    /// </summary>
    public DateTime LastSeen { get; set; }

    /// <summary>
    /// Total days of activity.
    /// </summary>
    public int TotalActiveDays { get; set; }

    /// <summary>
    /// Current dormancy (days since last activity).
    /// </summary>
    public int DaysSinceLast { get; set; }

    /// <summary>
    /// Total sessions in this period.
    /// </summary>
    public int TotalSessions { get; set; }

    /// <summary>
    /// Average sessions per day.
    /// </summary>
    public double AvgSessionsPerDay { get; set; }

    /// <summary>
    /// Is this account currently active (last seen < 7 days)?
    /// </summary>
    public bool IsCurrentlyActive => DaysSinceLast < 7;
}

/// <summary>
/// Gap analysis between two accounts' activity periods.
/// </summary>
public class GapAnalysis
{
    /// <summary>
    /// Days between the end of one account and start of another.
    /// Negative = overlap (they played simultaneously).
    /// Zero = back-to-back (one ended, other started same day).
    /// Positive = gap between them.
    /// </summary>
    public int DaysBetween { get; set; }

    /// <summary>
    /// Which account stopped first?
    /// </summary>
    public string AccountStoppedFirst { get; set; } = "";

    /// <summary>
    /// Which account started second?
    /// </summary>
    public string AccountStartedSecond { get; set; } = "";

    /// <summary>
    /// Date when first account went dormant.
    /// </summary>
    public DateTime SwitchoverStart { get; set; }

    /// <summary>
    /// Date when second account became active.
    /// </summary>
    public DateTime SwitchoverEnd { get; set; }

    /// <summary>
    /// Total switchover window (days).
    /// Small window = more suspicious (planned switchover).
    /// </summary>
    public int SwitchoverWindowDays { get; set; }

    /// <summary>
    /// How much activity overlap? (0-1).
    /// 0 = no overlap (clean switch).
    /// 1 = complete overlap (played simultaneously).
    /// </summary>
    public double OverlapRatio { get; set; }

    /// <summary>
    /// Pattern description.
    /// </summary>
    public string PatternDescription { get; set; } = "";
}

/// <summary>
/// Daily activity snapshot for timeline visualization.
/// </summary>
public class DailyActivity
{
    /// <summary>
    /// Date of this activity record.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Number of sessions on this day.
    /// </summary>
    public int SessionCount { get; set; }

    /// <summary>
    /// Total minutes played.
    /// </summary>
    public int TotalMinutes { get; set; }

    /// <summary>
    /// Average K/D for the day.
    /// </summary>
    public double AvgKd { get; set; }

    /// <summary>
    /// Was the player active on this day?
    /// </summary>
    public bool WasActive => SessionCount > 0;

    /// <summary>
    /// Intensity level for visualization (0-1).
    /// </summary>
    public double IntensityScore => Math.Min(1.0, SessionCount / 10.0 * (TotalMinutes / 1440.0));
}

/// <summary>
/// Visual timeline representation for CLI/web display.
/// </summary>
public class TimelineVisualization
{
    /// <summary>
    /// ASCII art timeline showing both accounts side by side.
    /// </summary>
    public string AsciiTimeline { get; set; } = "";

    /// <summary>
    /// JSON-serializable timeline data for charting libraries.
    /// </summary>
    public List<TimelineDataPoint> DataPoints { get; set; } = [];

    /// <summary>
    /// Key dates to highlight (switchover points, account creation, etc).
    /// </summary>
    public List<TimelineMarker> Markers { get; set; } = [];
}

/// <summary>
/// Single data point for timeline visualization.
/// </summary>
public class TimelineDataPoint
{
    public DateTime Date { get; set; }
    public string Account { get; set; } = "";
    public int SessionCount { get; set; }
    public int TotalMinutes { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Important event marker on timeline.
/// </summary>
public class TimelineMarker
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = "";
    public string Type { get; set; } = ""; // "creation", "lastSeen", "switchover", "flag"
    public string Account { get; set; } = "";
}
