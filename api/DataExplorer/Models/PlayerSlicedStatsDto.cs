namespace api.DataExplorer.Models;

/// <summary>
/// Response containing sliced player statistics with dimension control and pagination.
/// </summary>
public record PlayerSlicedStatsResponse(
    string PlayerName,
    string Game,
    string SliceDimension,
    SliceDimensionType SliceType,
    List<PlayerSliceResultDto> Results,
    DateRangeDto DateRange,
    PaginationDto Pagination
);

/// <summary>
/// Individual slice result for a player.
/// </summary>
public record PlayerSliceResultDto(
    string SliceKey,      // Map name, server name, etc.
    string? SubKey,       // Server name when slicing by map+server, null for map-only
    string? SubKeyLabel,  // Friendly label for the SubKey (e.g. Server Name)
    string SliceLabel,    // Human readable label
    int PrimaryValue,     // The main metric (wins, score, kills, etc.)
    int SecondaryValue,   // Supporting metric (total rounds, deaths, etc.)
    double Percentage,    // Percentage (win rate, etc.)
    int Rank,            // Player's rank in this slice
    int TotalPlayers,    // Total players in this slice
    Dictionary<string, object> AdditionalData // Flexible data for different slice types
);

/// <summary>
/// Pagination information for sliced results.
/// </summary>
public record PaginationDto(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasNext,
    bool HasPrevious
);

/// <summary>
/// Available slice dimension types.
/// </summary>
public enum SliceDimensionType
{
    WinsByMap,           // Player wins by map (cross-server)
    WinsByMapAndServer,  // Player wins by map+server combination
    TeamWinsByMap,       // Team wins by map (based on ServerMapStats)
    TeamWinsByMapAndServer, // Team wins by map+server
    ScoreByMap,          // Traditional score ranking by map
    ScoreByMapAndServer, // Traditional score ranking by map+server
    KillsByMap,          // Kills by map
    KillsByMapAndServer  // Kills by map+server
}

/// <summary>
/// Available slice dimensions for UI selection.
/// </summary>
public record SliceDimensionOption(
    SliceDimensionType Type,
    string Name,
    string Description
);