namespace api.DataExplorer.Models;

/// <summary>
/// Detailed map information including server rotation.
/// </summary>
public record MapDetailDto(
    string MapName,
    List<ServerOnMapDto> Servers,
    WinStatsDto AggregatedWinStats
);

/// <summary>
/// Server information for a specific map.
/// </summary>
public record ServerOnMapDto(
    string ServerGuid,
    string ServerName,
    string Game,
    bool IsOnline,
    int TotalRoundsOnMap,
    WinStatsDto WinStats
);
