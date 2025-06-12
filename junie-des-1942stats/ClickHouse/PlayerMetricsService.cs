using System.Net.Http;
using System.Text;
using System.Text.Json;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.ClickHouse;

public class PlayerMetricsService
{
    private readonly HttpClient _httpClient;
    private readonly string _clickHouseUrl;

    public PlayerMetricsService(HttpClient httpClient, string clickHouseUrl)
    {
        _httpClient = httpClient;
        _clickHouseUrl = clickHouseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Ensures the ClickHouse schema (tables and views) are created
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        try
        {
            await CreatePlayerMetricsTableAsync();
            await CreateDailyRankingsViewAsync();
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse schema verified/created successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to ensure ClickHouse schema: {ex.Message}");
            throw;
        }
    }

    private async Task CreatePlayerMetricsTableAsync()
    {
        var createTableQuery = @"
CREATE TABLE IF NOT EXISTS player_metrics (
    timestamp DateTime,
    server_guid String,
    server_name String,
    player_name String,
    score UInt32,
    kills UInt16,
    deaths UInt16,
    ping UInt16,
    team UInt8,
    map_name String,
    game_type String
) ENGINE = MergeTree()
ORDER BY (server_guid, timestamp)
PARTITION BY toYYYYMM(timestamp)";

        await ExecuteQueryAsync(createTableQuery);
    }

    private async Task CreateDailyRankingsViewAsync()
    {
        var createViewQuery = @"
CREATE MATERIALIZED VIEW IF NOT EXISTS daily_rankings 
ENGINE = AggregatingMergeTree()
ORDER BY (server_guid, date)
AS SELECT 
    server_guid,
    server_name,
    toDate(timestamp) as date,
    player_name,
    sumState(kills) as total_kills,
    sumState(deaths) as total_deaths,
    avgState(ping) as avg_ping
FROM player_metrics
GROUP BY server_guid, server_name, date, player_name";

        await ExecuteQueryAsync(createViewQuery);
    }

    private async Task ExecuteQueryAsync(string query)
    {
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"ClickHouse query failed: {response.StatusCode} - {errorContent}");
        }
    }

    public async Task StorePlayerMetricsAsync(IGameServer server, DateTime timestamp)
    {
        if (!server.Players.Any())
            return;

        var metrics = server.Players.Select(player => new PlayerMetric
        {
            Timestamp = timestamp,
            ServerGuid = server.Guid,
            ServerName = server.Name,
            PlayerName = player.Name,
            Score = (uint)Math.Max(0, player.Score),
            Kills = (ushort)Math.Max(0, player.Kills),
            Deaths = (ushort)Math.Max(0, player.Deaths),
            Ping = (ushort)Math.Max(0, player.Ping),
            Team = (byte)Math.Max(0, player.Team),
            MapName = server.MapName,
            GameType = server.GameType
        }).ToList();

        await InsertPlayerMetricsAsync(metrics);
    }

    public async Task StoreBatchedPlayerMetricsAsync(IEnumerable<IGameServer> servers, DateTime timestamp)
    {
        var allMetrics = new List<PlayerMetric>();

        foreach (var server in servers)
        {
            if (!server.Players.Any())
                continue;

            var serverMetrics = server.Players.Select(player => new PlayerMetric
            {
                Timestamp = timestamp,
                ServerGuid = server.Guid,
                ServerName = server.Name,
                PlayerName = player.Name,
                Score = (uint)Math.Max(0, player.Score),
                Kills = (ushort)Math.Max(0, player.Kills),
                Deaths = (ushort)Math.Max(0, player.Deaths),
                Ping = (ushort)Math.Max(0, player.Ping),
                Team = (byte)Math.Max(0, player.Team),
                MapName = server.MapName,
                GameType = server.GameType
            });

            allMetrics.AddRange(serverMetrics);
        }

        if (allMetrics.Any())
        {
            await InsertPlayerMetricsAsync(allMetrics);
        }
    }

    private async Task InsertPlayerMetricsAsync(List<PlayerMetric> metrics)
    {
        if (!metrics.Any())
            return;

        try
        {
            // Build the VALUES part of the INSERT statement
            var values = new StringBuilder();
            
            for (int i = 0; i < metrics.Count; i++)
            {
                var metric = metrics[i];
                
                if (i > 0)
                    values.Append(',');
                
                values.Append('(');
                values.Append($"'{metric.Timestamp:yyyy-MM-dd HH:mm:ss}',");
                values.Append($"'{EscapeString(metric.ServerGuid)}',");
                values.Append($"'{EscapeString(metric.ServerName)}',");
                values.Append($"'{EscapeString(metric.PlayerName)}',");
                values.Append($"{metric.Score},");
                values.Append($"{metric.Kills},");
                values.Append($"{metric.Deaths},");
                values.Append($"{metric.Ping},");
                values.Append($"{metric.Team},");
                values.Append($"'{EscapeString(metric.MapName)}',");
                values.Append($"'{EscapeString(metric.GameType)}'");
                values.Append(')');
            }

            var query = $"INSERT INTO player_metrics (timestamp, server_guid, server_name, player_name, score, kills, deaths, ping, team, map_name, game_type) VALUES {values}";
            
            var content = new StringContent(query, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse error: {response.StatusCode} - {errorContent}");
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Successfully stored {metrics.Count} player metrics to ClickHouse");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to store player metrics to ClickHouse: {ex.Message}");
        }
    }

    private static string EscapeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";
        
        return input.Replace("'", "\\'").Replace("\\", "\\\\");
    }
}

public class PlayerMetric
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public uint Score { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public ushort Ping { get; set; }
    public byte Team { get; set; }
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
} 