using api.DataExplorer.Models;

namespace api.DataExplorer;

public interface IDataExplorerService
{
    /// <summary>
    /// Get all servers with summary information, filtered by game.
    /// </summary>
    /// <param name="game">Game filter: bf1942 (default), fh2, or bfvietnam</param>
    Task<ServerListResponse> GetServersAsync(string game = "bf1942");

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
}
