using api.DataExplorer.Models;

namespace api.DataExplorer;

public interface IDataExplorerService
{
    /// <summary>
    /// Get paginated servers with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of results per page</param>
    Task<ServerListResponse> GetServersAsync(string game = "bf1942", int page = 1, int pageSize = 50);

    /// <summary>
    /// Get detailed information for a specific server.
    /// </summary>
    Task<ServerDetailDto?> GetServerDetailAsync(string serverGuid);

    /// <summary>
    /// Get paginated map rotation for a specific server.
    /// </summary>
    /// <param name="serverGuid">The server GUID</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of results per page</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    Task<MapRotationResponse?> GetServerMapRotationAsync(string serverGuid, int page = 1, int pageSize = 10, int days = 60);

    /// <summary>
    /// Get all maps with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<MapListResponse> GetMapsAsync(string game = "bf1942");

    /// <summary>
    /// Get detailed information for a specific map, filtered by game.
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<MapDetailDto?> GetMapDetailAsync(string mapName, string game = "bf1942");

    /// <summary>
    /// Get detailed information for a specific server-map combination.
    /// </summary>
    Task<ServerMapDetailDto?> GetServerMapDetailAsync(string serverGuid, string mapName, int days = 60);

    /// <summary>
    /// Search for players by name prefix, filtered by game.
    /// Requires at least 3 characters. Returns top 50 matches by score.
    /// </summary>
    /// <param name="query">Search query (min 3 characters)</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<PlayerSearchResponse> SearchPlayersAsync(string query, string game = "bf1942");

    /// <summary>
    /// Get player map rankings with per-server breakdown and rank information.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    Task<PlayerMapRankingsResponse?> GetPlayerMapRankingsAsync(string playerName, string game = "bf1942", int days = 60, string? serverGuid = null);

    /// <summary>
    /// Get paginated player rankings for a specific map (aggregated across all servers).
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of results per page</param>
    /// <param name="searchQuery">Optional player name search filter</param>
    /// <param name="serverGuid">Optional server GUID filter</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    /// <param name="sortBy">Sort field: score (default), kills, kdRatio, killRate</param>
    Task<MapPlayerRankingsResponse?> GetMapPlayerRankingsAsync(
        string mapName,
        string game = "bf1942",
        int page = 1,
        int pageSize = 10,
        string? searchQuery = null,
        string? serverGuid = null,
        int days = 60,
        string sortBy = "score");

    /// <summary>
    /// Get activity patterns for a specific map showing when it's typically played.
    /// Returns hourly patterns grouped by day of week.
    /// </summary>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<MapActivityPatternsResponse?> GetMapActivityPatternsAsync(string mapName, string game = "bf1942");

    /// <summary>
    /// Get randomized engagement stats for a specific server to encourage exploration.
    /// Returns 3 different randomized interesting statistics.
    /// </summary>
    /// <param name="serverGuid">The server GUID</param>
    Task<ServerEngagementStatsDto> GetServerEngagementStatsAsync(string serverGuid);

    /// <summary>
    /// Get randomized engagement stats for a specific player to encourage exploration.
    /// Returns 3 different randomized interesting statistics.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<PlayerEngagementStatsDto> GetPlayerEngagementStatsAsync(string playerName, string game = "bf1942");

    /// <summary>
    /// Get sliced player statistics with configurable dimensions and pagination.
    /// Enables advanced data exploration by different metrics and groupings.
    /// Returns an empty result set if no data is available, preserving player context.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="sliceType">The slice dimension type</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of results per page (default 20, max 100)</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    Task<PlayerSlicedStatsResponse> GetPlayerSlicedStatsAsync(
        string playerName,
        SliceDimensionType sliceType,
        string game = "bf1942",
        int page = 1,
        int pageSize = 20,
        int days = 60);

    /// <summary>
    /// Get available slice dimensions for the player data explorer.
    /// </summary>
    Task<List<SliceDimensionOption>> GetAvailableSliceDimensionsAsync();

    /// <summary>
    /// Get player's competitive rankings across all maps they've played.
    /// Shows rank position, percentile, and trends.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    Task<PlayerCompetitiveRankingsResponse?> GetPlayerCompetitiveRankingsAsync(
        string playerName, 
        string game = "bf1942", 
        int days = 60);

    /// <summary>
    /// Get player's ranking history over time for trend analysis.
    /// Returns monthly snapshots of rank positions.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="mapName">Optional: specific map to filter by</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="months">Number of months to look back (default 12)</param>
    Task<RankingTimelineResponse?> GetPlayerRankingTimelineAsync(
        string playerName,
        string? mapName = null,
        string game = "bf1942",
        int months = 12);

    /// <summary>
    /// Get player's detailed statistics for a specific map.
    /// Shows aggregated stats and breakdown by server.
    /// </summary>
    /// <param name="playerName">The player name</param>
    /// <param name="mapName">The map name</param>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    /// <param name="days">Number of days to look back (default 60)</param>
    Task<PlayerMapDetailResponse?> GetPlayerMapStatsAsync(
        string playerName,
        string mapName,
        string game = "bf1942",
        int days = 60);
}
