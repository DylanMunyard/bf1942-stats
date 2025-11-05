using api.ClickHouse.Models;
using api.PlayerStats.Models;

namespace api.ClickHouse;

/// <summary>
/// Interface for player insights service.
/// Provides methods to retrieve player-specific insights and milestones.
/// </summary>
public interface IPlayerInsightsService
{
    /// <summary>
    /// Executes a custom ClickHouse query asynchronously.
    /// </summary>
    /// <param name="query">The ClickHouse query to execute.</param>
    /// <returns>The raw query result as a string.</returns>
    Task<string> ExecuteQueryAsync(string query);

    /// <summary>
    /// Gets kill milestones for one or more players (5k, 10k, 20k, 50k, 75k, 100k kills).
    /// </summary>
    /// <param name="playerNames">List of player names to retrieve milestones for.</param>
    /// <returns>List of kill milestone records for the specified players.</returns>
    Task<List<PlayerKillMilestone>> GetPlayersKillMilestonesAsync(List<string> playerNames);

    /// <summary>
    /// Gets server-specific insights for a player (servers with 10+ hours).
    /// </summary>
    /// <param name="playerName">The player name to get insights for.</param>
    /// <returns>List of server insights for the player.</returns>
    Task<List<ServerInsight>> GetPlayerServerInsightsAsync(string playerName);

    /// <summary>
    /// Gets kill milestones for a single player (convenience method).
    /// </summary>
    /// <param name="playerName">The player name to get milestones for.</param>
    /// <returns>List of kill milestones for the player.</returns>
    Task<List<KillMilestone>> GetPlayerKillMilestonesAsync(string playerName);
}
