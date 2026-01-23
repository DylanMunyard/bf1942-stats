using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Weekly activity patterns for games.
/// 168 rows per game (7 days × 24 hours).
/// Used by GetWeeklyActivityPatternsAsync.
/// </summary>
public class HourlyActivityPattern
{
    public required string Game { get; set; } // bf1942, fh2, bfvietnam
    public int DayOfWeek { get; set; } // 0=Sunday, 6=Saturday (SQLite convention)
    public int HourOfDay { get; set; } // 0-23
    public double UniquePlayersAvg { get; set; }
    public double TotalRoundsAvg { get; set; }
    public double AvgRoundDuration { get; set; }
    public required string PeriodType { get; set; } // 'Weekend' or 'Weekday'
    public Instant UpdatedAt { get; set; }
}
