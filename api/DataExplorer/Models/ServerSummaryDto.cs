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
/// Response containing list of servers with pagination info.
/// </summary>
public record ServerListResponse(
    List<ServerSummaryDto> Servers,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore
);
