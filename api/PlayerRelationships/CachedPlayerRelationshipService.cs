using api.PlayerRelationships.Models;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

/// <summary>
/// Cached wrapper for PlayerRelationshipService.
/// </summary>
public class CachedPlayerRelationshipService(
    PlayerRelationshipService innerService,
    IRelationshipCacheService cacheService,
    ILogger<CachedPlayerRelationshipService> logger) : IPlayerRelationshipService
{
    public async Task<List<PlayerRelationship>> GetMostFrequentCoPlayersAsync(
        string playerName, 
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"player:{playerName}:teammates:{limit}";
        var cached = await cacheService.GetAsync<List<PlayerRelationship>>(cacheKey, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for teammates of {PlayerName}", playerName);
            return cached;
        }

        var result = await innerService.GetMostFrequentCoPlayersAsync(playerName, limit, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30), cancellationToken);
        
        return result;
    }

    public Task<List<string>> GetPotentialConnectionsAsync(
        string playerName,
        int limit = 20,
        int daysActive = 30,
        CancellationToken cancellationToken = default)
    {
        // Don't cache potential connections as they should be fresh
        return innerService.GetPotentialConnectionsAsync(playerName, limit, daysActive, cancellationToken);
    }

    public async Task<List<string>> GetSharedServersAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"relationship:{player1Name}:{player2Name}:servers";
        var cached = await cacheService.GetAsync<List<string>>(cacheKey, cancellationToken);
        
        if (cached != null)
            return cached;

        var result = await innerService.GetSharedServersAsync(player1Name, player2Name, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);
        
        return result;
    }

    public Task<List<PlayerRelationship>> GetRecentConnectionsAsync(
        string playerName,
        int daysSince = 7,
        CancellationToken cancellationToken = default)
    {
        // Don't cache recent connections as they change frequently
        return innerService.GetRecentConnectionsAsync(playerName, daysSince, cancellationToken);
    }

    public async Task<PlayerRelationship?> GetRelationshipAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"relationship:{player1Name}:{player2Name}";
        var cached = await cacheService.GetAsync<PlayerRelationship>(cacheKey, cancellationToken);
        
        if (cached != null)
            return cached;

        var result = await innerService.GetRelationshipAsync(player1Name, player2Name, cancellationToken);
        
        if (result != null)
            await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);
        
        return result;
    }

    public async Task<PlayerNetworkStats> GetPlayerNetworkStatsAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var cached = await cacheService.GetPlayerNetworkStatsAsync(playerName, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for network stats of {PlayerName}", playerName);
            return cached;
        }

        var result = await innerService.GetPlayerNetworkStatsAsync(playerName, cancellationToken);
        await cacheService.SetPlayerNetworkStatsAsync(playerName, result, cancellationToken);
        
        return result;
    }

    public async Task<PlayerNetworkGraph> GetPlayerNetworkGraphAsync(
        string playerName,
        int depth = 2,
        int maxNodes = 100,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"player:{playerName}:network-graph:{depth}:{maxNodes}";
        var cached = await cacheService.GetAsync<PlayerNetworkGraph>(cacheKey, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for network graph of {PlayerName}", playerName);
            return cached;
        }

        var result = await innerService.GetPlayerNetworkGraphAsync(playerName, depth, maxNodes, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        
        return result;
    }

    public async Task<ServerSocialStats> GetServerSocialStatsAsync(
        string serverGuid,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"server:{serverGuid}:social-stats";
        var cached = await cacheService.GetAsync<ServerSocialStats>(cacheKey, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for social stats of server {ServerGuid}", serverGuid);
            return cached;
        }

        var result = await innerService.GetServerSocialStatsAsync(serverGuid, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);
        
        return result;
    }

    public async Task<List<PlayerCommunity>> GetCommunitiesAsync(
        int minSize = 3,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        // Only cache the "all communities" query
        if (minSize == 3 && activeOnly)
        {
            var cached = await cacheService.GetCommunitiesAsync(cancellationToken);
            if (cached != null)
            {
                logger.LogDebug("Cache hit for communities");
                return cached;
            }
        }

        var result = await innerService.GetCommunitiesAsync(minSize, activeOnly, cancellationToken);
        
        if (minSize == 3 && activeOnly)
        {
            await cacheService.SetCommunitiesAsync(result, cancellationToken);
        }
        
        return result;
    }

    public Task<PlayerCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken cancellationToken = default)
    {
        // Communities are cached as a list, so we'll use the inner service
        return innerService.GetCommunityByIdAsync(communityId, cancellationToken);
    }

    public async Task<List<PlayerCommunity>> GetPlayerCommunitiesAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"player:{playerName}:communities";
        var cached = await cacheService.GetAsync<List<PlayerCommunity>>(cacheKey, cancellationToken);
        
        if (cached != null)
            return cached;

        var result = await innerService.GetPlayerCommunitiesAsync(playerName, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);
        
        return result;
    }

    public async Task<string> DetectAndStoreCommunities(CancellationToken cancellationToken = default)
    {
        // Invalidate community cache before detection
        await cacheService.RemoveAsync("communities:all", cancellationToken);
        
        var result = await innerService.DetectAndStoreCommunities(cancellationToken);
        
        // Pre-populate the cache with fresh data
        var communities = await innerService.GetCommunitiesAsync(cancellationToken: cancellationToken);
        await cacheService.SetCommunitiesAsync(communities, cancellationToken);
        
        return result;
    }

    public Task<List<SquadRecommendation>> GetSquadRecommendationsAsync(
        string playerName,
        int limit = 10,
        bool onlineOnly = false,
        CancellationToken cancellationToken = default)
    {
        // Don't cache squad recommendations as they should be fresh and personalized
        return innerService.GetSquadRecommendationsAsync(playerName, limit, onlineOnly, cancellationToken);
    }

    public Task RecordSquadRecommendationFeedback(
        string playerName,
        string recommendedPlayer,
        bool wasHelpful,
        CancellationToken cancellationToken = default)
    {
        // No caching needed for feedback recording
        return innerService.RecordSquadRecommendationFeedback(playerName, recommendedPlayer, wasHelpful, cancellationToken);
    }

    public async Task<PlayerMigrationFlow> GetPlayerMigrationFlowAsync(
        DateTime startDate,
        DateTime endDate,
        string? game = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"migration-flow:{startDate:yyyy-MM-dd}:{endDate:yyyy-MM-dd}:{game ?? "all"}";
        var cached = await cacheService.GetAsync<PlayerMigrationFlow>(cacheKey, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for migration flow");
            return cached;
        }

        var result = await innerService.GetPlayerMigrationFlowAsync(startDate, endDate, game, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(6), cancellationToken);
        
        return result;
    }

    public async Task<List<ServerNode>> GetServerLifecycleAnalysisAsync(
        int daysBack = 90,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"server-lifecycle:{daysBack}";
        var cached = await cacheService.GetAsync<List<ServerNode>>(cacheKey, cancellationToken);
        
        if (cached != null)
        {
            logger.LogDebug("Cache hit for server lifecycle analysis");
            return cached;
        }

        var result = await innerService.GetServerLifecycleAnalysisAsync(daysBack, cancellationToken);
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromHours(12), cancellationToken);
        
        return result;
    }
}