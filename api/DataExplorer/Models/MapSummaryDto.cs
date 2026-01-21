namespace api.DataExplorer.Models;

/// <summary>
/// Summary information for a map in the list view.
/// </summary>
public record MapSummaryDto(
    string MapName,
    int ServersPlayingCount,
    int TotalRoundsLast30Days,
    double AvgPlayersWhenPlayed
);

/// <summary>
/// Response containing list of maps.
/// </summary>
public record MapListResponse(
    List<MapSummaryDto> Maps,
    int TotalCount
);
