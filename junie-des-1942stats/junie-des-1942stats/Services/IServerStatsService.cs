using junie_des_1942stats.ServerStats.Models;

namespace junie_des_1942stats.Services;

/// <summary>
/// Interface for server statistics service.
/// Provides methods to retrieve server stats, rankings, leaderboards, and insights.
/// </summary>
public interface IServerStatsService
{
    /// <summary>
    /// Gets detailed server statistics for a specific server and time period.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="days">Number of days to include in statistics.</param>
    /// <returns>Server statistics including player counts and performance metrics.</returns>
    Task<ServerStatistics> GetServerStatistics(string serverName, int days);

    /// <summary>
    /// Gets server leaderboards for a specific time period.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="days">Number of days to include.</param>
    /// <param name="minPlayersForWeighting">Optional minimum players for weighting calculations.</param>
    /// <returns>Server leaderboards with top players.</returns>
    Task<ServerLeaderboards> GetServerLeaderboards(
        string serverName,
        int days,
        int? minPlayersForWeighting = null);

    /// <summary>
    /// Gets server rankings with pagination and filtering.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="year">Optional year to filter by.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="playerName">Optional player name filter.</param>
    /// <param name="minScore">Optional minimum score filter.</param>
    /// <param name="minKills">Optional minimum kills filter.</param>
    /// <param name="minDeaths">Optional minimum deaths filter.</param>
    /// <param name="minKdRatio">Optional minimum K/D ratio filter.</param>
    /// <param name="minPlayTimeMinutes">Optional minimum play time filter.</param>
    /// <param name="orderBy">Field to order by.</param>
    /// <param name="orderDirection">Order direction ('asc' or 'desc').</param>
    /// <returns>Paginated server rankings.</returns>
    Task<PagedResult<ServerRanking>> GetServerRankings(
        string serverName,
        int? year,
        int page,
        int pageSize,
        string? playerName,
        int? minScore,
        int? minKills,
        int? minDeaths,
        double? minKdRatio,
        int? minPlayTimeMinutes,
        string? orderBy,
        string? orderDirection);

    /// <summary>
    /// Gets insights about server activity and trends.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="days">Number of days to include in analysis.</param>
    /// <returns>Server insights with trends and analysis.</returns>
    Task<ServerInsights> GetServerInsights(string serverName, int days);

    /// <summary>
    /// Gets insights about maps played on the server.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="days">Number of days to include in analysis.</param>
    /// <returns>Server maps insights with usage statistics.</returns>
    Task<ServerMapsInsights> GetServerMapsInsights(string serverName, int days);

    /// <summary>
    /// Gets all servers with pagination, sorting, and filtering.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="sortBy">Field to sort by.</param>
    /// <param name="sortOrder">Order direction ('asc' or 'desc').</param>
    /// <param name="filters">Filters to apply.</param>
    /// <returns>Paginated list of servers.</returns>
    Task<PagedResult<ServerBasicInfo>> GetAllServersWithPaging(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        ServerFilters filters);
}
