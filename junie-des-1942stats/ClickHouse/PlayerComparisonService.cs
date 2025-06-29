using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Readers;
using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class PlayerComparisonService
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<PlayerComparisonService> _logger;
    private readonly PlayerTrackerDbContext _dbContext;

    public PlayerComparisonService(ClickHouseConnection connection, ILogger<PlayerComparisonService> logger, PlayerTrackerDbContext dbContext)
    {
        _connection = connection;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<PlayerComparisonResult> ComparePlayersAsync(string player1, string player2, string serverGuid = null)
    {
        var result = new PlayerComparisonResult
        {
            Player1 = player1,
            Player2 = player2
        };

        // If serverGuid is provided, look up server details
        if (!string.IsNullOrEmpty(serverGuid))
        {
            var server = await _dbContext.Servers
                .Where(s => s.Guid == serverGuid)
                .Select(s => new ServerDetails
                {
                    Guid = s.Guid,
                    Name = s.Name,
                    Ip = s.Ip,
                    Port = s.Port,
                    GameId = s.GameId,
                    Country = s.Country,
                    Region = s.Region,
                    City = s.City,
                    Timezone = s.Timezone,
                    Org = s.Org
                })
                .FirstOrDefaultAsync();
            
            result.ServerDetails = server;
        }

        await EnsureConnectionOpenAsync();

        // 1. Kill Rate (per minute)
        result.KillRates = await GetKillRates(player1, player2, serverGuid);

        // 2. Totals in Buckets
        result.BucketTotals = await GetBucketTotals(player1, player2, serverGuid);

        // 3. Average Ping
        result.AveragePing = await GetAveragePing(player1, player2, serverGuid);

        // 4. Map Performance
        result.MapPerformance = await GetMapPerformance(player1, player2, serverGuid);

        // 5. Overlapping Sessions (Head-to-Head)
        result.HeadToHead = await GetHeadToHead(player1, player2, serverGuid);

        // 6. Common Servers (servers where both players have played)
        result.CommonServers = await GetCommonServers(player1, player2);

        return result;
    }

    private async Task EnsureConnectionOpenAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
    }

    private async Task<List<KillRateComparison>> GetKillRates(string player1, string player2, string serverGuid = null)
    {
        // Calculate kill rate (kills per minute) for each player
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
WITH diffs AS (
    SELECT
        player_name,
        timestamp,
        kills - lagInFrame(kills, 1, 0) OVER (PARTITION BY player_name, server_guid ORDER BY timestamp) AS kills_diff,
        dateDiff('minute', lagInFrame(timestamp, 1, timestamp) OVER (PARTITION BY player_name, server_guid ORDER BY timestamp), timestamp) AS minutes_diff
    FROM player_metrics
    WHERE player_name IN ({Quote(player1)}, {Quote(player2)}){serverFilter}
)
SELECT player_name, sum(kills_diff) / nullIf(sum(minutes_diff), 0) AS kill_rate
FROM diffs
WHERE minutes_diff > 0 AND kills_diff >= 0
GROUP BY player_name";

        var result = new List<KillRateComparison>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new KillRateComparison
            {
                PlayerName = reader.GetString(0),
                KillRate = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1))
            });
        }
        return result;
    }

    private async Task<List<BucketTotalsComparison>> GetBucketTotals(string player1, string player2, string serverGuid = null)
    {
        // Buckets: last 30 days, last 6 months, last year, all time
        var buckets = new[]
        {
            ("Last30Days", "timestamp >= now() - INTERVAL 30 DAY"),
            ("Last6Months", "timestamp >= now() - INTERVAL 6 MONTH"),
            ("LastYear", "timestamp >= now() - INTERVAL 1 YEAR"),
            ("AllTime", "1=1")
        };
        var results = new List<BucketTotalsComparison>();
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        foreach (var (label, condition) in buckets)
        {
            var query = $@"
WITH round_sessions AS (
    SELECT *,
        (kills < lagInFrame(kills, 1, 0) OVER w OR 
         deaths < lagInFrame(deaths, 1, 0) OVER w OR
         map_name != lagInFrame(map_name, 1, map_name) OVER w OR
         dateDiff('minute', lagInFrame(timestamp, 1, timestamp) OVER w, timestamp) >= 15 OR
         ROW_NUMBER() OVER w = 1) AS is_round_start
    FROM player_metrics
    WHERE player_name IN ({Quote(player1)}, {Quote(player2)}) AND {condition}{serverFilter}
    WINDOW w AS (PARTITION BY player_name ORDER BY map_name, timestamp)
),
round_numbers AS (
    SELECT *,
        SUM(CASE WHEN is_round_start THEN 1 ELSE 0 END) OVER 
        (PARTITION BY player_name ORDER BY timestamp ROWS UNBOUNDED PRECEDING) AS round_id
    FROM round_sessions
),
round_totals AS (
    SELECT player_name, round_id, server_guid, map_name,
        MAX(score) AS final_score,
        MAX(kills) AS final_kills, 
        MAX(deaths) AS final_deaths,
        dateDiff('minute', MIN(timestamp), MAX(timestamp)) AS play_time_minutes
    FROM round_numbers
    GROUP BY player_name, round_id, server_guid, map_name
)
SELECT player_name, 
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM round_totals
GROUP BY player_name";
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = query;
            await using var reader = await cmd.ExecuteReaderAsync();
            var bucket = new BucketTotalsComparison { Bucket = label };
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var totals = new PlayerTotals
                {
                    Score = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                    Kills = reader.IsDBNull(2) ? 0u : Convert.ToUInt32(reader.GetValue(2)),
                    Deaths = reader.IsDBNull(3) ? 0u : Convert.ToUInt32(reader.GetValue(3)),
                    PlayTimeMinutes = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetValue(4))
                };
                if (name == player1) bucket.Player1Totals = totals;
                else if (name == player2) bucket.Player2Totals = totals;
            }
            results.Add(bucket);
        }
        return results;
    }

    private async Task<List<PingComparison>> GetAveragePing(string player1, string player2, string serverGuid = null)
    {
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
SELECT player_name, avg(ping)
FROM (
    SELECT player_name, ping
    FROM player_metrics 
    WHERE player_name IN ({Quote(player1)}, {Quote(player2)}) AND ping > 0{serverFilter}
    ORDER BY timestamp DESC
    LIMIT 300 BY player_name
)
GROUP BY player_name";
        var result = new List<PingComparison>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PingComparison
            {
                PlayerName = reader.GetString(0),
                AveragePing = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1))
            });
        }
        return result;
    }

    private async Task<List<MapPerformanceComparison>> GetMapPerformance(string player1, string player2, string serverGuid = null)
    {
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
WITH round_sessions AS (
    SELECT *,
        (kills < lagInFrame(kills, 1, 0) OVER w OR 
         deaths < lagInFrame(deaths, 1, 0) OVER w OR
         map_name != lagInFrame(map_name, 1, map_name) OVER w OR
         dateDiff('minute', lagInFrame(timestamp, 1, timestamp) OVER w, timestamp) >= 15 OR
         ROW_NUMBER() OVER w = 1) AS is_round_start
    FROM player_metrics
    WHERE player_name IN ({Quote(player1)}, {Quote(player2)}){serverFilter}
    WINDOW w AS (PARTITION BY player_name ORDER BY map_name, timestamp)
),
round_numbers AS (
    SELECT *,
        SUM(CASE WHEN is_round_start THEN 1 ELSE 0 END) OVER 
        (PARTITION BY player_name ORDER BY timestamp ROWS UNBOUNDED PRECEDING) AS round_id
    FROM round_sessions
),
round_totals AS (
    SELECT player_name, round_id, server_guid, map_name,
        MAX(score) AS final_score,
        MAX(kills) AS final_kills, 
        MAX(deaths) AS final_deaths,
        dateDiff('minute', MIN(timestamp), MAX(timestamp)) AS play_time_minutes
    FROM round_numbers
    GROUP BY player_name, round_id, server_guid, map_name
)
SELECT map_name, player_name, 
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM round_totals
GROUP BY map_name, player_name";
        var mapStats = new Dictionary<string, MapPerformanceComparison>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var map = reader.GetString(0);
            var name = reader.GetString(1);
            var score = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
            var kills = reader.IsDBNull(3) ? 0u : Convert.ToUInt32(reader.GetValue(3));
            var deaths = reader.IsDBNull(4) ? 0u : Convert.ToUInt32(reader.GetValue(4));
            var playTime = reader.IsDBNull(5) ? 0.0 : Convert.ToDouble(reader.GetValue(5));
            if (!mapStats.ContainsKey(map))
                mapStats[map] = new MapPerformanceComparison { MapName = map };
            if (name == player1)
                mapStats[map].Player1Totals = new PlayerTotals { Score = score, Kills = kills, Deaths = deaths, PlayTimeMinutes = playTime };
            else if (name == player2)
                mapStats[map].Player2Totals = new PlayerTotals { Score = score, Kills = kills, Deaths = deaths, PlayTimeMinutes = playTime };
        }
        return mapStats.Values.ToList();
    }

    private async Task<List<HeadToHeadSession>> GetHeadToHead(string player1, string player2, string serverGuid = null)
    {
        // Find overlapping rounds using round detection
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
WITH p1_rounds AS (
    SELECT *,
        (kills < lagInFrame(kills, 1, 0) OVER w OR 
         deaths < lagInFrame(deaths, 1, 0) OVER w OR
         map_name != lagInFrame(map_name, 1, map_name) OVER w OR
         dateDiff('minute', lagInFrame(timestamp, 1, timestamp) OVER w, timestamp) >= 15 OR
         ROW_NUMBER() OVER w = 1) AS is_round_start
    FROM player_metrics
    WHERE player_name = {Quote(player1)}{serverFilter}
    WINDOW w AS (ORDER BY map_name, timestamp)
),
p1_numbered AS (
    SELECT *,
        SUM(CASE WHEN is_round_start THEN 1 ELSE 0 END) OVER 
        (ORDER BY timestamp ROWS UNBOUNDED PRECEDING) AS round_id
    FROM p1_rounds
),
p1_final AS (
    SELECT round_id, server_guid, map_name, 
        MIN(timestamp) AS round_start,
        MAX(timestamp) AS round_end,
        MAX(score) AS final_score,
        MAX(kills) AS final_kills,
        MAX(deaths) AS final_deaths
    FROM p1_numbered
    GROUP BY round_id, server_guid, map_name
),
p2_rounds AS (
    SELECT *,
        (kills < lagInFrame(kills, 1, 0) OVER w OR 
         deaths < lagInFrame(deaths, 1, 0) OVER w OR
         map_name != lagInFrame(map_name, 1, map_name) OVER w OR
         dateDiff('minute', lagInFrame(timestamp, 1, timestamp) OVER w, timestamp) >= 15 OR
         ROW_NUMBER() OVER w = 1) AS is_round_start
    FROM player_metrics
    WHERE player_name = {Quote(player2)}{serverFilter}
    WINDOW w AS (ORDER BY map_name, timestamp)
),
p2_numbered AS (
    SELECT *,
        SUM(CASE WHEN is_round_start THEN 1 ELSE 0 END) OVER 
        (ORDER BY timestamp ROWS UNBOUNDED PRECEDING) AS round_id
    FROM p2_rounds
),
p2_final AS (
    SELECT round_id, server_guid, map_name, 
        MIN(timestamp) AS round_start,
        MAX(timestamp) AS round_end,
        MAX(score) AS final_score,
        MAX(kills) AS final_kills,
        MAX(deaths) AS final_deaths
    FROM p2_numbered
    GROUP BY round_id, server_guid, map_name
)
SELECT p1.round_start, p1.round_end, p1.server_guid, p1.map_name,
       p1.final_score, p1.final_kills, p1.final_deaths,
       p2.final_score, p2.final_kills, p2.final_deaths
FROM p1_final p1
JOIN p2_final p2 ON p1.server_guid = p2.server_guid 
    AND p1.map_name = p2.map_name
    AND p1.round_start <= p2.round_end 
    AND p2.round_start <= p1.round_end
ORDER BY p1.round_start DESC
LIMIT 50";
        var sessions = new List<HeadToHeadSession>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new HeadToHeadSession
            {
                Timestamp = reader.GetDateTime(0),
                ServerGuid = reader.GetString(2),
                MapName = reader.GetString(3),
                Player1Score = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                Player1Kills = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                Player1Deaths = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6)),
                Player2Score = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                Player2Kills = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
                Player2Deaths = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9))
            });
        }
        return sessions;
    }

    private async Task<List<ServerDetails>> GetCommonServers(string player1, string player2)
    {
        // Get servers where both players have played in the last 6 months
        var query = $@"
SELECT DISTINCT server_guid
FROM player_metrics
WHERE player_name = {Quote(player1)} AND timestamp >= now() - INTERVAL 6 MONTH
INTERSECT
SELECT DISTINCT server_guid
FROM player_metrics
WHERE player_name = {Quote(player2)} AND timestamp >= now() - INTERVAL 6 MONTH";

        var serverGuids = new List<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            serverGuids.Add(reader.GetString(0));
        }

        // Get server details from the database
        if (serverGuids.Any())
        {
            var servers = await _dbContext.Servers
                .Where(s => serverGuids.Contains(s.Guid))
                .Select(s => new ServerDetails
                {
                    Guid = s.Guid,
                    Name = s.Name,
                    Ip = s.Ip,
                    Port = s.Port,
                    GameId = s.GameId,
                    Country = s.Country,
                    Region = s.Region,
                    City = s.City,
                    Timezone = s.Timezone,
                    Org = s.Org
                })
                .ToListAsync();
            
            return servers;
        }

        return new List<ServerDetails>();
    }

    private static string Quote(string s) => $"'{s.Replace("'", "''")}'";
}

// Result Models
public class PlayerComparisonResult
{
    public string Player1 { get; set; }
    public string Player2 { get; set; }
    public ServerDetails? ServerDetails { get; set; }
    public List<KillRateComparison> KillRates { get; set; } = new();
    public List<BucketTotalsComparison> BucketTotals { get; set; } = new();
    public List<PingComparison> AveragePing { get; set; } = new();
    public List<MapPerformanceComparison> MapPerformance { get; set; } = new();
    public List<HeadToHeadSession> HeadToHead { get; set; } = new();
    public List<ServerDetails> CommonServers { get; set; } = new();
}

public class KillRateComparison
{
    public string PlayerName { get; set; }
    public double KillRate { get; set; }
}

public class BucketTotalsComparison
{
    public string Bucket { get; set; }
    public PlayerTotals Player1Totals { get; set; } = new();
    public PlayerTotals Player2Totals { get; set; } = new();
}

public class PlayerTotals
{
    public int Score { get; set; }
    public uint Kills { get; set; }
    public uint Deaths { get; set; }
    public double PlayTimeMinutes { get; set; }
}

public class PingComparison
{
    public string PlayerName { get; set; }
    public double AveragePing { get; set; }
}

public class MapPerformanceComparison
{
    public string MapName { get; set; }
    public PlayerTotals Player1Totals { get; set; } = new();
    public PlayerTotals Player2Totals { get; set; } = new();
}

public class HeadToHeadSession
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; }
    public string MapName { get; set; }
    public int Player1Score { get; set; }
    public int Player1Kills { get; set; }
    public int Player1Deaths { get; set; }
    public int Player2Score { get; set; }
    public int Player2Kills { get; set; }
    public int Player2Deaths { get; set; }
}

public class ServerDetails
{
    public string Guid { get; set; }
    public string Name { get; set; }
    public string Ip { get; set; }
    public int Port { get; set; }
    public string GameId { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public string? Org { get; set; }
} 