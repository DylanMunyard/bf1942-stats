namespace api.DataExplorer.Models;

/// <summary>
/// Response containing player stats grouped by map with server breakdowns.
/// </summary>
public record PlayerMapRankingsResponse(
    string PlayerName,
    string Game,
    PlayerOverallStatsDto OverallStats,
    List<PlayerMapGroupDto> MapGroups,
    List<NumberOneRankingDto> NumberOneRankings,
    DateRangeDto DateRange
);

/// <summary>
/// Overall aggregated stats for a player.
/// </summary>
public record PlayerOverallStatsDto(
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    int TotalRounds,
    int UniqueServers,
    int UniqueMaps
);

/// <summary>
/// Player stats for a specific map with server breakdowns.
/// </summary>
public record PlayerMapGroupDto(
    string MapName,
    int AggregatedScore,
    List<PlayerServerStatsDto> ServerStats,
    int? BestRank,
    string? BestRankServer
);

/// <summary>
/// Player stats on a specific server for a specific map, including rank.
/// </summary>
public record PlayerServerStatsDto(
    string ServerGuid,
    string ServerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    int TotalRounds,
    int Rank
);

/// <summary>
/// Represents a map/server where the player is ranked #1.
/// </summary>
public record NumberOneRankingDto(
    string MapName,
    string ServerName,
    string ServerGuid,
    int TotalScore
);
