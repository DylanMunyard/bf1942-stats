namespace api.DataExplorer.Models;

/// <summary>
/// Detailed server information including map rotation and activity patterns.
/// </summary>
public record ServerDetailDto(
    string Guid,
    string Name,
    string Game,
    string? Country,
    bool IsOnline,
    List<MapRotationItemDto> MapRotation,
    WinStatsDto OverallWinStats,
    List<PerMapStatsDto> PerMapStats,
    List<ActivityPatternDto> ActivityPatterns
);

/// <summary>
/// Map rotation item with play statistics.
/// </summary>
public record MapRotationItemDto(
    string MapName,
    int TotalRounds,
    double PlayTimePercentage,
    double AvgConcurrentPlayers,
    WinStatsDto WinStats
);

/// <summary>
/// Per-map statistics including win stats and top players.
/// </summary>
public record PerMapStatsDto(
    string MapName,
    WinStatsDto WinStats,
    List<TopPlayerDto> TopPlayers
);

/// <summary>
/// Top player information for leaderboard preview.
/// </summary>
public record TopPlayerDto(
    string PlayerName,
    int TotalScore,
    int TotalKills,
    double KdRatio
);

/// <summary>
/// Activity pattern for busy hours heatmap.
/// </summary>
public record ActivityPatternDto(
    int DayOfWeek,
    int HourOfDay,
    double AvgPlayers,
    double MedianPlayers
);
