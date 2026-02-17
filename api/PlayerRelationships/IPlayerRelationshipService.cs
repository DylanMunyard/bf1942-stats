using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Service for querying player relationships from Neo4j graph database.
/// </summary>
public interface IPlayerRelationshipService
{
    /// <summary>
    /// Get players who most frequently play with the specified player.
    /// </summary>
    Task<List<PlayerRelationship>> GetMostFrequentCoPlayersAsync(
        string playerName, 
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get players who play on the same servers but have never played together.
    /// </summary>
    Task<List<string>> GetPotentialConnectionsAsync(
        string playerName,
        int limit = 20,
        int daysActive = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all servers where two players have played together.
    /// </summary>
    Task<List<string>> GetSharedServersAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find recent new connections (players who started playing together recently).
    /// </summary>
    Task<List<PlayerRelationship>> GetRecentConnectionsAsync(
        string playerName,
        int daysSince = 7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get relationship strength between two players.
    /// Returns null if they've never played together.
    /// </summary>
    Task<PlayerRelationship?> GetRelationshipAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default);
}
