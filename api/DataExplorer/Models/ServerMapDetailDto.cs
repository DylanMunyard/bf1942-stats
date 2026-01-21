namespace api.DataExplorer.Models;

public record ServerMapDetailDto(
    string ServerGuid,
    string ServerName,
    string MapName,
    string Game,
    bool IsServerOnline,
    MapActivityStatsDto MapActivity,
    WinStatsDto WinStats,
    List<LeaderboardEntryDto> TopByScore,
    List<LeaderboardEntryDto> TopByKills,
    List<LeaderboardEntryDto> TopByKdRatio,
    List<LeaderboardEntryDto> TopByKillRate,
    DateRangeDto DateRange
);

public record MapActivityStatsDto(
    int TotalRounds,
    int TotalPlayTimeMinutes,
    double AvgConcurrentPlayers,
    int PeakConcurrentPlayers
);

public record LeaderboardEntryDto(
    string PlayerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    double KillsPerMinute,
    int TotalRounds,
    double PlayTimeMinutes
);

public record DateRangeDto(
    int Days,
    DateTime FromDate,
    DateTime ToDate
);
