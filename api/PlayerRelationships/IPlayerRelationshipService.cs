using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Service for querying player relationships from Neo4j graph database.
/// </summary>
public interface IPlayerRelationshipService
{
    Task<List<PlayerRelationship>> GetMostFrequentCoPlayersAsync(
        string playerName, 
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetPotentialConnectionsAsync(
        string playerName,
        int limit = 20,
        int daysActive = 30,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetSharedServersAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default);

    Task<List<PlayerRelationship>> GetRecentConnectionsAsync(
        string playerName,
        int daysSince = 7,
        CancellationToken cancellationToken = default);

    Task<PlayerRelationship?> GetRelationshipAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default);

    Task<PlayerNetworkStats> GetPlayerNetworkStatsAsync(
        string playerName,
        CancellationToken cancellationToken = default);

    Task<PlayerNetworkGraph> GetPlayerNetworkGraphAsync(
        string playerName,
        int depth = 2,
        int maxNodes = 100,
        CancellationToken cancellationToken = default);
}
