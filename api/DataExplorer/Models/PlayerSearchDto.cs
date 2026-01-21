namespace api.DataExplorer.Models;

/// <summary>
/// Response containing list of players matching search criteria.
/// </summary>
public record PlayerSearchResponse(
    List<PlayerSearchResultDto> Players,
    int TotalCount,
    string Query
);

/// <summary>
/// Summary information for a player in search results.
/// </summary>
public record PlayerSearchResultDto(
    string PlayerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    int TotalRounds,
    int UniqueMaps,
    int UniqueServers
);
