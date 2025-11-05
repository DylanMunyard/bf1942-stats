using api.Players.Models;

namespace api.Players;

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
    /// Gets insights about a player's play patterns and trends.
    /// </summary>
    /// <param name="playerName">The name of the player.</param>
    /// <param name="startDate">Optional start date for analysis period.</param>
    /// <param name="endDate">Optional end date for analysis period.</param>
    /// <param name="daysToAnalyze">Optional number of days to analyze (if startDate/endDate not provided).</param>
    /// <returns>Player insights with activity analysis.</returns>
    Task<PlayerInsights> GetPlayerInsights(
        string playerName,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? daysToAnalyze = null);

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
