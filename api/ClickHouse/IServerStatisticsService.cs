using api.ClickHouse.Models;

namespace api.ClickHouse;

public interface IServerStatisticsService : IDisposable
{
    Task<List<ServerStatistics>> GetServerStats(string playerName, TimePeriod period, string serverGuid);
}
