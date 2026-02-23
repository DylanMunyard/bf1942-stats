using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Caching service for player relationship data.
/// </summary>
public interface IRelationshipCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    
    Task<PlayerNetworkStats?> GetPlayerNetworkStatsAsync(string playerName, CancellationToken cancellationToken = default);
    
    Task SetPlayerNetworkStatsAsync(string playerName, PlayerNetworkStats stats, CancellationToken cancellationToken = default);
    
    Task<List<PlayerCommunity>?> GetCommunitiesAsync(CancellationToken cancellationToken = default);
    
    Task SetCommunitiesAsync(List<PlayerCommunity> communities, CancellationToken cancellationToken = default);
    
    Task InvalidatePlayerDataAsync(string playerName, CancellationToken cancellationToken = default);
    
    Task InvalidateServerDataAsync(string serverGuid, CancellationToken cancellationToken = default);
}