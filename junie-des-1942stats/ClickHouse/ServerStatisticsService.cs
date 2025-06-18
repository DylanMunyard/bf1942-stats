using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClickHouse.Client;
using ClickHouse.Client.ADO;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class ServerStatisticsService : IDisposable
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<ServerStatisticsService> _logger;
    private bool _disposed;

    public ServerStatisticsService(IConfiguration configuration, ILogger<ServerStatisticsService> logger)
    {
        _logger = logger;
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        
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
            TimePeriod.LastMonth => @"AND timestamp >= toStartOfMonth(now() - INTERVAL 1 MONTH)
                                    AND timestamp < toStartOfMonth(now())",
            _ => throw new ArgumentException("Invalid time period", nameof(period))
        };
    }

    public async Task<List<ServerStatistics>> GetServerStats(string playerName, TimePeriod period, string serverGuid = null)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var serverFilter = string.IsNullOrEmpty(serverGuid) ? "" : $"AND server_guid = '{serverGuid}'";
            var timePeriodCondition = GetTimePeriodCondition(period);

            var query = $@"
WITH session_boundaries AS (
    SELECT 
        player_name,
        map_name,
        server_guid,
        server_name,
        timestamp,
        score,
        kills,
        deaths,
        CASE 
            WHEN kills < prev_kills OR timestamp > prev_timestamp + INTERVAL 1 HOUR 
            THEN 1 
            ELSE 0 
        END AS is_new_session
    FROM (
        SELECT 
            *,
            lagInFrame(kills, 1, 0) OVER (PARTITION BY player_name, map_name, server_guid ORDER BY timestamp) AS prev_kills,
            lagInFrame(timestamp, 1, timestamp) OVER (PARTITION BY player_name, map_name, server_guid ORDER BY timestamp) AS prev_timestamp
        FROM player_metrics
        WHERE player_name = '{playerName}'
        {serverFilter}
        {timePeriodCondition}
        ORDER BY player_name, map_name, server_guid, timestamp
    )
    ORDER BY player_name, map_name, server_guid, server_name, timestamp
),
sessions AS (
    SELECT 
        *,
        sum(is_new_session) OVER (PARTITION BY player_name, map_name, server_guid ORDER BY timestamp) AS session_id
    FROM session_boundaries
    ORDER BY player_name, map_name, server_guid, timestamp
)
SELECT 
    server_name,
    map_name,
    sum(max_score) AS total_score,
    sum(max_kills) AS total_kills,
    sum(max_deaths) AS total_deaths,
    count(DISTINCT server_guid, session_id) AS sessions_played,
    sum(session_duration_minutes) AS total_play_time_minutes
FROM (
    SELECT 
        map_name,
        server_guid,
        server_name,
        session_id,
        max(score) AS max_score,
        max(kills) AS max_kills,
        max(deaths) AS max_deaths,
        dateDiff('minute', min(timestamp), max(timestamp)) AS session_duration_minutes
    FROM sessions
    GROUP BY map_name, server_guid, server_name, session_id
)
GROUP BY map_name, server_name
ORDER BY total_kills DESC";

            var results = new List<ServerStatistics>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ServerStatistics
                {
                    ServerName = reader.GetString(0),
                    MapName = reader.GetString(1),
                    TotalScore = Convert.ToInt32(reader.GetValue(2)),
                    TotalKills = Convert.ToInt32(reader.GetValue(3)),
                    TotalDeaths = Convert.ToInt32(reader.GetValue(4)),
                    SessionsPlayed = Convert.ToInt32(reader.GetValue(5)),
                    TotalPlayTimeMinutes = Convert.ToInt32(reader.GetValue(6))
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