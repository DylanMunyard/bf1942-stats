using junie_des_1942stats.PlayerStats.Models;

namespace junie_des_1942stats.Services;

/// <summary>
/// Interface for player statistics service.
/// Provides methods to retrieve player stats, rankings, and comparisons.
/// </summary>
public interface IPlayerStatsService
{
    /// <summary>
    /// Gets detailed statistics for a specific player.
    /// </summary>
    /// <param name="playerName">The name of the player.</param>
    /// <returns>Comprehensive player statistics including servers, map stats, and insights.</returns>
    Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName);

    /// <summary>
    /// Gets details about a specific player session.
    /// </summary>
    /// <param name="playerName">The name of the player.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>Session details or null if not found.</returns>
    Task<SessionDetail?> GetSession(string playerName, int sessionId);

    /// <summary>
    /// Gets all players with pagination, sorting, and filtering.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="sortBy">Field to sort by.</param>
    /// <param name="sortOrder">Order direction ('asc' or 'desc').</param>
    /// <param name="filters">Filters to apply.</param>
    /// <returns>Paginated list of players.</returns>
    Task<PagedResult<PlayerBasicInfo>> GetAllPlayersWithPaging(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        PlayerFilters filters);
}
