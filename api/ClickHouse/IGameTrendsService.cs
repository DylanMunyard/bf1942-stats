using api.ClickHouse.Models;

namespace api.ClickHouse;

public interface IGameTrendsService
{
    Task<List<CurrentActivityStatus>> GetCurrentActivityStatusAsync(string? game = null, string[]? serverGuids = null);
    Task<List<WeeklyActivityPattern>> GetWeeklyActivityPatternsAsync(string? game = null, int daysPeriod = 30);
    Task<SmartPredictionInsights> GetSmartPredictionInsightsAsync(string? game = null);
    Task<GroupedServerBusyIndicatorResult> GetServerBusyIndicatorAsync(string[] serverGuids, int timelineHourRange = 4);
}
