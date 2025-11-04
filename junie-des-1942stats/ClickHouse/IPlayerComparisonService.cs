namespace junie_des_1942stats.ClickHouse;

/// <summary>
/// Interface for player comparison service.
/// Provides methods to compare players and find similar players.
/// </summary>
public interface IPlayerComparisonService
{
    /// <summary>
    /// Compares two players and returns detailed comparison data.
    /// </summary>
    /// <param name="player1">Name of the first player.</param>
    /// <param name="player2">Name of the second player.</param>
    /// <param name="serverGuid">Optional server GUID to limit comparison to specific server.</param>
    /// <returns>Comprehensive comparison result.</returns>
    Task<PlayerComparisonResult> ComparePlayersAsync(string player1, string player2, string? serverGuid = null);

    /// <summary>
    /// Compares activity hours (time of day) between two players.
    /// </summary>
    /// <param name="player1">Name of the first player.</param>
    /// <param name="player2">Name of the second player.</param>
    /// <returns>Comparison of when each player is active.</returns>
    Task<PlayerActivityHoursComparison> ComparePlayersActivityHoursAsync(string player1, string player2);

    /// <summary>
    /// Finds players similar to the specified player based on play style and statistics.
    /// </summary>
    /// <param name="targetPlayer">The player to find similarities for.</param>
    /// <param name="limit">Maximum number of similar players to return.</param>
    /// <param name="filterBySimilarOnlineTime">Whether to filter by similar online times.</param>
    /// <param name="mode">Similarity matching mode.</param>
    /// <returns>Result containing target player and list of similar players.</returns>
    Task<SimilarPlayersResult> FindSimilarPlayersAsync(
        string targetPlayer,
        int limit = 10,
        bool filterBySimilarOnlineTime = true,
        SimilarityMode mode = SimilarityMode.Default);
}
