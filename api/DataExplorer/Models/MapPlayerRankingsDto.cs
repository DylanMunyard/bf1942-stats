namespace api.DataExplorer.Models;

/// <summary>
/// Response for paginated map player rankings across all servers.
/// </summary>
public record MapPlayerRankingsResponse(
    string MapName,
    string Game,
    List<MapPlayerRankingDto> Rankings,
    int TotalCount,
    int Page,
    int PageSize,
    DateRangeDto DateRange
);

/// <summary>
/// Individual player ranking entry for a map (aggregated across all servers).
/// </summary>
public record MapPlayerRankingDto(
    int Rank,
    string PlayerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    double KillsPerMinute,
    int TotalRounds,
    double PlayTimeMinutes,
    int UniqueServers,
    int TotalWins = 0
);
