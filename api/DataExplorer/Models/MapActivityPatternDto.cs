namespace api.DataExplorer.Models;

/// <summary>
/// Activity pattern for a map at a specific day/hour slot.
/// Used for "When is this map played?" heatmaps.
/// </summary>
public record MapActivityPatternDto(
    int DayOfWeek,     // 0=Sunday, 6=Saturday
    int HourOfDay,     // 0-23 (UTC)
    double AvgPlayers, // Average player count when map is played at this time
    int TimesPlayed    // Number of times map was played at this time slot
);

/// <summary>
/// Response containing activity patterns for a map.
/// </summary>
public record MapActivityPatternsResponse(
    string MapName,
    string Game,
    List<MapActivityPatternDto> ActivityPatterns,
    int TotalDataPoints // Total number of distinct days with data
);
