namespace api.DataExplorer.Models;

/// <summary>
/// Response containing player's competitive rankings across all maps.
/// </summary>
public record PlayerCompetitiveRankingsResponse(
    string PlayerName,
    string Game,
    List<MapRankingDto> MapRankings,
    RankingSummaryDto Summary,
    DateRangeDto DateRange
);

/// <summary>
/// Player's ranking information for a specific map.
/// </summary>
public record MapRankingDto(
    string MapName,
    int Rank,
    int TotalPlayers,
    double Percentile,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    int TotalRounds,
    double PlayTimeMinutes,
    string Trend, // "up", "down", "stable", "new"
    int? PreviousRank
);

/// <summary>
/// Summary statistics about player's rankings.
/// </summary>
public record RankingSummaryDto(
    int TotalMapsPlayed,
    int Top1Rankings,
    int Top10Rankings,  
    int Top25Rankings,
    int Top100Rankings,
    double AveragePercentile,
    string? BestRankedMap,
    int? BestRank,
    string PercentileCategory // "elite", "master", "expert", "veteran", "regular"
);

/// <summary>
/// Response containing player's ranking history over time.
/// </summary>
public record RankingTimelineResponse(
    string PlayerName,
    string? MapName,
    string Game,
    List<RankingSnapshotDto> Timeline
);

/// <summary>
/// Historical ranking snapshot for a specific time period.
/// </summary>
public record RankingSnapshotDto(
    int Year,
    int Month,
    string MonthLabel, // e.g., "Jan 2024"
    int Rank,
    int TotalPlayers,
    double Percentile,
    int TotalScore,
    double KdRatio,
    bool HasData
);