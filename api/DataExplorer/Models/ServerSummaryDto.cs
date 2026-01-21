namespace api.DataExplorer.Models;

/// <summary>
/// Summary information for a server in the list view.
/// </summary>
public record ServerSummaryDto(
    string Guid,
    string Name,
    string Game,
    string? Country,
    bool IsOnline,
    int CurrentPlayers,
    int MaxPlayers,
    int TotalMaps,
    int TotalRoundsLast30Days
);

/// <summary>
/// Response containing list of servers.
/// </summary>
public record ServerListResponse(
    List<ServerSummaryDto> Servers,
    int TotalCount
);
