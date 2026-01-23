using api.Bflist.Models;
using api.Analytics.Models;

namespace api.GameTrends;

/// <summary>
/// SQLite-based implementation of game trends service.
/// Queries pre-computed tables for analytics.
/// </summary>
public interface ISqliteGameTrendsService
{
    /// <summary>
    /// Gets smart prediction insights for the next 8 hours based on historical patterns.
    /// </summary>
    Task<SmartPredictionInsights> GetSmartPredictionInsightsAsync(string? game = null);

    /// <summary>
    /// Gets busy indicators for servers showing Google-style "how busy is it" information.
    /// </summary>
    Task<GroupedServerBusyIndicatorResult> GetServerBusyIndicatorAsync(string[] serverGuids, int timelineHourRange = 4);

    /// <summary>
    /// Gets players online history data for charts.
    /// </summary>
    Task<PlayersOnlineHistoryResponse> GetPlayersOnlineHistoryAsync(string game, string period, int rollingWindowDays, string? serverGuid = null);

    /// <summary>
    /// Gets weekly activity patterns (168 hour slots).
    /// </summary>
    Task<List<WeeklyActivityPattern>> GetWeeklyActivityPatternsAsync(string? game = null, int daysPeriod = 30);
}
