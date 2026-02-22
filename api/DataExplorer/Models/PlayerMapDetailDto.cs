namespace api.DataExplorer.Models;

/// <summary>
/// Response containing player's detailed statistics for a specific map.
/// </summary>
public record PlayerMapDetailResponse(
    string PlayerName,
    string MapName,
    string Game,
    PlayerMapAggregatedStats AggregatedStats,
    List<PlayerMapServerBreakdown> ServerBreakdown,
    DateRangeDto DateRange
);

/// <summary>
/// Aggregated player statistics for a map across all servers.
/// </summary>
public record PlayerMapAggregatedStats(
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    int TotalRounds,
    double PlayTimeMinutes
);

/// <summary>
/// Player statistics breakdown by server for a specific map.
/// </summary>
public record PlayerMapServerBreakdown(
    string ServerGuid,
    string ServerName,
    int Score,
    int Kills,
    int Deaths,
    int Rounds,
    double PlayTime
);