using api.DataExplorer.Models;

namespace api.DataExplorer;

public interface IDataExplorerService
{
    /// <summary>
    /// Get all servers with summary information.
    /// </summary>
    Task<ServerListResponse> GetServersAsync();

    /// <summary>
    /// Get detailed information for a specific server.
    /// </summary>
    Task<ServerDetailDto?> GetServerDetailAsync(string serverGuid);

    /// <summary>
    /// Get all maps with summary information.
    /// </summary>
    Task<MapListResponse> GetMapsAsync();

    /// <summary>
    /// Get detailed information for a specific map.
    /// </summary>
    Task<MapDetailDto?> GetMapDetailAsync(string mapName);
}
