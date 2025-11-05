using ClickHouse.Client.ADO;
using api.ClickHouse.Models;
using Microsoft.Extensions.Logging;

namespace api.ClickHouse;

public class RealTimeAnalyticsService(ILogger<RealTimeAnalyticsService> logger) : IDisposable
{
    private readonly ILogger<RealTimeAnalyticsService> _logger = logger;
    private readonly ClickHouseConnection _connection = InitializeConnection(logger);
    private bool _disposed;

    private static ClickHouseConnection InitializeConnection(ILogger<RealTimeAnalyticsService> logger)
    {
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        try
        {
            var uri = new Uri(clickHouseUrl);
            var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
            var connection = new ClickHouseConnection(connectionString);
            logger.LogInformation("ClickHouse connection initialized with URL: {Url}", clickHouseUrl);
            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize ClickHouse connection with URL: {Url}", clickHouseUrl);
            throw;
        }
    }

    public async Task<List<TeamKillerMetrics>> MonitorTeamkillers()
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            // NOTE: This query CANNOT be optimized with player_rounds table because:
            // 1. We need real-time point-in-time snapshots to detect score drops
            // 2. We need to analyze timestamp-ordered sequences to detect team killing patterns
            // 3. player_rounds only contains final round stats, not the intermediate snapshots
            // 4. The complex window functions detect session resets and score deltas in real-time
            var query = @"
-- Step 1: Detect session resets and calculate deltas
WITH reset_detection AS (
    SELECT 
        timestamp,
        server_name,
        server_guid,
        player_name,
        team_name,
        map_name,
        score,
        kills,
        deaths,
        lagInFrame(score, 1, 0) OVER w1 as prev_score,
        lagInFrame(kills, 1, 0) OVER w1 as prev_kills,
        lagInFrame(deaths, 1, 0) OVER w1 as prev_deaths,
        -- Detect session resets
        if(
            -- Reset conditions: metrics go to 0 when they were previously > 0
            (score = 0 AND lagInFrame(score, 1, 0) OVER w1 > 0) OR
            (kills = 0 AND lagInFrame(kills, 1, 0) OVER w1 > 0) OR
            (deaths = 0 AND lagInFrame(deaths, 1, 0) OVER w1 > 0) OR
            -- Detect large drops that indicate reset (adjust thresholds as needed)
            (score < lagInFrame(score, 1, 0) OVER w1 - 50 AND lagInFrame(score, 1, 0) OVER w1 > 0) OR
            (kills < lagInFrame(kills, 1, 0) OVER w1 - 10 AND lagInFrame(kills, 1, 0) OVER w1 > 0),
            1, 0
        ) as is_reset
    FROM player_metrics
    WHERE timestamp >= now() - INTERVAL 30 MINUTE
    WINDOW w1 AS (PARTITION BY server_guid, player_name, map_name ORDER BY timestamp)
),

-- Step 2: Create session IDs based on resets
session_groups AS (
    SELECT
        timestamp,
        server_name,
        server_guid,
        player_name,
        team_name,
        map_name,
        score,
        kills,
        deaths,
        prev_score,
        prev_kills,
        prev_deaths,
        sum(is_reset) OVER (PARTITION BY server_guid, player_name, map_name ORDER BY timestamp 
                           ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) as session_id
    FROM reset_detection
),

-- Step 3: Add previous session ID for delta calculation
session_with_prev AS (
    SELECT
        timestamp,
        server_name,
        server_guid,
        player_name,
        team_name,
        map_name,
        score,
        kills,
        deaths,
        prev_score,
        prev_kills,
        prev_deaths,
        session_id,
        lagInFrame(session_id, 1) OVER w2 as prev_session_id
    FROM session_groups
    WINDOW w2 AS (PARTITION BY server_guid, player_name, map_name ORDER BY timestamp)
),

-- Step 4: Calculate deltas only within same session
deltas AS (
    SELECT
        timestamp,
        server_name,
        server_guid,
        player_name,
        team_name,
        map_name,
        score,
        kills,
        deaths,
        session_id,
        -- Calculate deltas only within the same session
        if(session_id = prev_session_id AND prev_session_id IS NOT NULL, 
           score - prev_score, 
           0) AS score_delta,
        if(session_id = prev_session_id AND prev_session_id IS NOT NULL, 
           kills - prev_kills, 
           0) AS kills_delta,
        if(session_id = prev_session_id AND prev_session_id IS NOT NULL, 
           deaths - prev_deaths, 
           0) AS deaths_delta
    FROM session_with_prev
),

-- Step 5: Aggregate team killer metrics
aggregated AS (
    SELECT
        server_name,
        server_guid,
        player_name,
        team_name,
        map_name,
        argMax(score, timestamp) as current_score,
        argMax(kills, timestamp) as current_kills,
        argMax(deaths, timestamp) as current_deaths,
        max(timestamp) as last_activity,
        -- Count unexplained score drops in last 10 minutes
        sum(if(
            timestamp >= now() - INTERVAL 10 MINUTE AND
            score_delta < 0 AND
            kills_delta = 0 AND
            deaths_delta = 0, 1, 0
        )) as unexplained_drops_last_10min,
        -- Total penalties in last 10 minutes
        sum(if(
            timestamp >= now() - INTERVAL 10 MINUTE AND
            score_delta < 0,
            abs(score_delta), 0
        )) as total_penalties_last_10min,
        -- Team killer likelihood calculation
        case
            when sum(if(
                timestamp >= now() - INTERVAL 10 MINUTE AND
                score_delta < 0 AND deaths_delta = 0, 1, 0
            )) >= 3 then 0.95
            when sum(if(
                timestamp >= now() - INTERVAL 10 MINUTE AND
                score_delta < 0 AND deaths_delta = 0, 1, 0
            )) >= 2 then 0.75
            when sum(if(
                timestamp >= now() - INTERVAL 10 MINUTE AND
                score_delta < -10 AND deaths_delta = 0, 1, 0
            )) >= 1 then 0.65
            else 0.0
        end as team_killer_likelihood
    FROM deltas
    GROUP BY server_name, server_guid, player_name, team_name, map_name
)

-- Final query
SELECT
    server_guid,
    server_name,
    player_name,
    team_name,
    map_name,
    current_score,
    current_kills,
    current_deaths,
    unexplained_drops_last_10min,
    total_penalties_last_10min,
    round(team_killer_likelihood, 3) as tk_probability,
    last_activity
FROM aggregated
WHERE team_killer_likelihood > 0.6 OR unexplained_drops_last_10min > 0
ORDER BY total_penalties_last_10min DESC, unexplained_drops_last_10min DESC";

            var results = new List<TeamKillerMetrics>();

            await using var command = _connection.CreateCommand();
            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new TeamKillerMetrics
                {
                    ServerGuid = reader.GetString(0),
                    ServerName = reader.GetString(1),
                    PlayerName = reader.GetString(2),
                    TeamName = reader.GetString(3),
                    MapName = reader.GetString(4),
                    CurrentScore = Convert.ToInt32(reader.GetValue(5)),
                    CurrentKills = Convert.ToUInt16(reader.GetValue(6)),
                    CurrentDeaths = Convert.ToUInt16(reader.GetValue(7)),
                    UnexplainedDropsLast10Min = Convert.ToInt32(reader.GetValue(8)),
                    TotalPenaltiesLast10Min = Convert.ToInt32(reader.GetValue(9)),
                    TkProbability = Convert.ToDouble(reader.GetValue(10)),
                    LastActivity = reader.GetDateTime(11)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving teamkiller data");
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
