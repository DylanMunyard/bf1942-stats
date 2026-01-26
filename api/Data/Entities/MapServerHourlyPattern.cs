using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed hourly activity patterns for maps per server.
/// Used to show "When is this map played?" heatmaps in the Data Explorer.
/// Can aggregate across all servers or show server-specific patterns.
/// Aggregated from Rounds table, grouped by server guid, map name, game, day of week, and hour.
/// </summary>
public class MapServerHourlyPattern
{
    public required string ServerGuid { get; set; }  // NEW
    public required string MapName { get; set; }
    public required string Game { get; set; }
    public int DayOfWeek { get; set; } // 0=Sunday, 6=Saturday (SQLite convention)
    public int HourOfDay { get; set; } // 0-23
    public double AvgPlayers { get; set; }
    public int TimesPlayed { get; set; }
    public int DataPoints { get; set; } // Number of distinct days with data
    public Instant UpdatedAt { get; set; }
}
