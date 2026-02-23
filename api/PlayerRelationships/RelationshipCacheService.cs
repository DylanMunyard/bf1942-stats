using System.Text.Json;
using api.PlayerRelationships.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

/// <summary>
/// Redis-based caching service for player relationship data.
/// </summary>
public class RelationshipCacheService(
    IDistributedCache cache,
    ILogger<RelationshipCacheService> logger) : IRelationshipCacheService
{
    private const string KeyPrefix = "bf1942:relationships:";
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(1);
    private static readonly TimeSpan CommunityExpiration = TimeSpan.FromHours(24);
    private static readonly TimeSpan NetworkStatsExpiration = TimeSpan.FromHours(2);

    private static string MakeKey(string key) => $"{KeyPrefix}{key}";

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var fullKey = MakeKey(key);
            var cached = await cache.GetStringAsync(fullKey, cancellationToken);
            
            if (cached == null)
                return null;

            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting cached value for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var fullKey = MakeKey(key);
            var json = JsonSerializer.Serialize(value);
            
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration
            };

            await cache.SetStringAsync(fullKey, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting cached value for key {Key}", key);
            // Don't throw - caching errors shouldn't break the application
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullKey = MakeKey(key);
            await cache.RemoveAsync(fullKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing cached value for key {Key}", key);
        }
    }

    public async Task<PlayerNetworkStats?> GetPlayerNetworkStatsAsync(string playerName, CancellationToken cancellationToken = default)
    {
        var key = $"player:{playerName}:network-stats";
        return await GetAsync<PlayerNetworkStats>(key, cancellationToken);
    }

    public async Task SetPlayerNetworkStatsAsync(string playerName, PlayerNetworkStats stats, CancellationToken cancellationToken = default)
    {
        var key = $"player:{playerName}:network-stats";
        await SetAsync(key, stats, NetworkStatsExpiration, cancellationToken);
    }

    public async Task<List<PlayerCommunity>?> GetCommunitiesAsync(CancellationToken cancellationToken = default)
    {
        var key = "communities:all";
        return await GetAsync<List<PlayerCommunity>>(key, cancellationToken);
    }

    public async Task SetCommunitiesAsync(List<PlayerCommunity> communities, CancellationToken cancellationToken = default)
    {
        var key = "communities:all";
        await SetAsync(key, communities, CommunityExpiration, cancellationToken);
    }

    public async Task InvalidatePlayerDataAsync(string playerName, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>
        {
            RemoveAsync($"player:{playerName}:network-stats", cancellationToken),
            RemoveAsync($"player:{playerName}:network-graph", cancellationToken),
            RemoveAsync($"player:{playerName}:teammates", cancellationToken),
            RemoveAsync($"player:{playerName}:communities", cancellationToken)
        };

        await Task.WhenAll(tasks);
    }

    public async Task InvalidateServerDataAsync(string serverGuid, CancellationToken cancellationToken = default)
    {
        await RemoveAsync($"server:{serverGuid}:social-stats", cancellationToken);
    }
}