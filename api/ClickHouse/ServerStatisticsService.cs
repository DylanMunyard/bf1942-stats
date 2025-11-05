using ClickHouse.Client.ADO;
using Microsoft.Extensions.Logging;
using api.ClickHouse.Models;
using api.Telemetry;

namespace api.ClickHouse;

public class ServerStatisticsService : IServerStatisticsService
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<ServerStatisticsService> _logger;
    private bool _disposed;

    public ServerStatisticsService(ILogger<ServerStatisticsService> logger)
    {
        _logger = logger;
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        try
        {
            var uri = new Uri(clickHouseUrl);
            var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
            _connection = new ClickHouseConnection(connectionString);
            _logger.LogInformation("ClickHouse connection initialized with URL: {Url}", clickHouseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ClickHouse connection with URL: {Url}", clickHouseUrl);
            throw;
        }
    }

    private string GetTimePeriodCondition(TimePeriod period)
    {
        return period switch
        {
            TimePeriod.ThisYear => "AND timestamp >= toDate(concat(toString(toYear(now())), '-01-01'))",
            TimePeriod.LastYear => @"AND timestamp >= toDate(concat(toString(toYear(now()) - 1), '-01-01'))
                                   AND timestamp < toDate(concat(toString(toYear(now())), '-01-01'))",
            TimePeriod.Last30Days => @"AND timestamp >= now() - INTERVAL 30 DAY
                                    AND timestamp < now()",
            _ => throw new ArgumentException("Invalid time period", nameof(period))
        };
    }

    public async Task<List<ServerStatistics>> GetServerStats(string playerName, TimePeriod period, string serverGuid)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("GetServerStats");
        activity?.SetTag("player.name", playerName);
        activity?.SetTag("server.guid", serverGuid);
        activity?.SetTag("time.period", period.ToString());
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var serverFilter = string.IsNullOrEmpty(serverGuid) ? "" : $" AND server_guid = {ClickHouseHelpers.QuoteString(serverGuid)}";
            var timePeriodCondition = GetTimePeriodCondition(period);

            // Optimized query using player_rounds table - much simpler and faster
            var query = $@"
SELECT 
    map_name,
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    COUNT(*) AS sessions_played,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name = {ClickHouseHelpers.QuoteString(playerName)}{serverFilter}
{timePeriodCondition.Replace("timestamp", "round_start_time")}
GROUP BY map_name
ORDER BY total_kills DESC";

            var results = new List<ServerStatistics>();

            await using var command = _connection.CreateCommand();
            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ServerStatistics
                {
                    MapName = reader.GetString(0),
                    TotalScore = Convert.ToInt32(reader.GetValue(1)),
                    TotalKills = Convert.ToInt32(reader.GetValue(2)),
                    TotalDeaths = Convert.ToInt32(reader.GetValue(3)),
                    SessionsPlayed = Convert.ToInt32(reader.GetValue(4)),
                    TotalPlayTimeMinutes = Convert.ToInt32(reader.GetValue(5))
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving server statistics for player {PlayerName}", playerName);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }
}
