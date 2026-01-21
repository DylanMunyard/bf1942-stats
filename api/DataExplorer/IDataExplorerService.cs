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
    Task<PlayerMapRankingsResponse?> GetPlayerMapRankingsAsync(string playerName, string game = "bf1942", int days = 60);

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
}
