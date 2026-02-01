using api.Analytics.Models;
using api.Players.Models;
using ServerStatistics = api.Analytics.Models.ServerStatistics;

namespace api.PlayerStats;

/// <summary>
/// SQLite-based player stats service that queries pre-computed tables.
/// </summary>
public interface ISqlitePlayerStatsService
{
    /// <summary>
    /// Gets lifetime statistics for a player.
    /// </summary>
    /// <param name="lookBackDays">Only include data from the last N days. Default 30. Use 0 or less for all time.</param>
    Task<PlayerLifetimeStats?> GetPlayerStatsAsync(string playerName, int lookBackDays = 30);

    /// <summary>
    /// Gets player's stats broken down by map.
    /// </summary>
    Task<List<ServerStatistics>> GetPlayerMapStatsAsync(
        string playerName,
        TimePeriod period,
        string? serverGuid = null);

    /// <summary>
    /// Gets player's insights per server (servers with 10+ hours playtime).
    /// </summary>
    /// <param name="lookBackDays">Only include data from the last N days. Default 30. Use 0 or less for all time.</param>
    Task<List<ServerInsight>> GetPlayerServerInsightsAsync(string playerName, int lookBackDays = 30);

    /// <summary>
    /// Gets player's top 3 scores for each time period.
    /// </summary>
    /// <param name="lookBackDays">Only include rounds from the last N days. Default 30. Use 0 or less for all time.</param>
    Task<PlayerBestScores> GetPlayerBestScoresAsync(string playerName, int lookBackDays = 30);

    /// <summary>
    /// Gets average ping for players over the last 7 days.
    /// </summary>
    Task<Dictionary<string, double>> GetAveragePingAsync(string[] playerNames);
}

/// <summary>
/// Player lifetime statistics model for SQLite-based queries.
/// </summary>
public class PlayerLifetimeStats
{
    public string PlayerName { get; set; } = "";
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double AvgScorePerRound { get; set; }
    public double KdRatio { get; set; }
    public double KillRate { get; set; }
    public DateTime FirstRoundTime { get; set; }
    public DateTime LastRoundTime { get; set; }
}
