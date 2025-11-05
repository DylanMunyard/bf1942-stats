using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using api.ClickHouse.Interfaces;
using api.ClickHouse.Base;
using api.ClickHouse.Models;

namespace api.ClickHouse;

public class PlayerMetricsWriteService(HttpClient httpClient, string clickHouseUrl) : BaseClickHouseService(httpClient, clickHouseUrl), IClickHouseWriter
{

    /// <summary>
    /// Ensures the ClickHouse schema (tables and views) are created
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        try
        {
            await CreatePlayerMetricsTableAsync();
            await CreateServerOnlineCountsTableAsync();
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
        var createV2 = @"
CREATE TABLE IF NOT EXISTS player_metrics
(
    timestamp   DateTime,
    server_guid String,
    player_name String,

    server_name String,
    score       Int32,
    kills       UInt16,
    deaths      UInt16,
    ping        UInt16,
    team_name   String,
    map_name    String,
    game_type   String,
    is_bot      UInt8,
    game        String
)
ENGINE = ReplacingMergeTree()
PARTITION BY toYYYYMM(timestamp)
ORDER BY (timestamp, server_guid, player_name)";
        await ExecuteCommandAsync(createV2);

        // Add game column if it doesn't exist (for existing tables)
        await ExecuteCommandAsync("ALTER TABLE player_metrics ADD COLUMN IF NOT EXISTS game String DEFAULT 'unknown'");
    }

    private async Task CreateServerOnlineCountsTableAsync()
    {
        var createTableQuery = @"
CREATE TABLE IF NOT EXISTS server_online_counts (
    timestamp DateTime,
    server_guid String,
    server_name String,
    players_online UInt16,
    map_name String,
    game String
) ENGINE = ReplacingMergeTree()
ORDER BY (server_guid, timestamp, game)
PARTITION BY toYYYYMM(timestamp)";

        await ExecuteCommandAsync(createTableQuery);
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }

    // Public bulk insert helper for precomputed metrics
    public async Task WritePlayerMetricsAsync(IEnumerable<PlayerMetric> metrics)
    {
        var list = metrics?.ToList() ?? new List<PlayerMetric>();
        if (list.Count == 0)
        {
            return;
        }
        await InsertPlayerMetricsAsync(list);
    }

    // Public bulk insert helper for precomputed server online counts
    public async Task WriteServerOnlineCountsAsync(IEnumerable<ServerOnlineCount> onlineCounts)
    {
        var list = onlineCounts?.ToList() ?? new List<ServerOnlineCount>();
        if (list.Count == 0)
        {
            return;
        }
        await InsertServerOnlineCountsAsync(list);
    }

    private async Task InsertServerOnlineCountsAsync(List<ServerOnlineCount> onlineCounts)
    {
        if (!onlineCounts.Any())
            return;

        try
        {
            using var stringWriter = new StringWriter();
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            // Sort to match ClickHouse PARTITION/ORDER BY for more efficient inserts
            var ordered = onlineCounts
                .OrderBy(c => c.Timestamp.Year)
                .ThenBy(c => c.Timestamp.Month)
                .ThenBy(c => c.ServerGuid)
                .ThenBy(c => c.Timestamp)
                .ThenBy(c => c.Game);

            csvWriter.WriteRecords(ordered.Select(c => new
            {
                Timestamp = c.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ServerGuid = c.ServerGuid,
                ServerName = c.ServerName,
                PlayersOnline = c.PlayersOnline,
                MapName = c.MapName,
                Game = c.Game
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO server_online_counts (timestamp, server_guid, server_name, players_online, map_name, game) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            await ExecuteCommandAsync(fullRequest);
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Successfully stored {onlineCounts.Count} server online counts to ClickHouse");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to store server online counts to ClickHouse: {ex.Message}");
        }
    }

    private async Task InsertPlayerMetricsAsync(List<PlayerMetric> metrics)
    {
        if (!metrics.Any())
            return;

        try
        {
            // Use CsvHelper to generate properly formatted CSV data
            using var stringWriter = new StringWriter();

            // Configure CsvHelper to not write headers
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            csvWriter.WriteRecords(metrics.Select(m => new
            {
                Timestamp = m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ServerGuid = m.ServerGuid,
                PlayerName = m.PlayerName,
                ServerName = m.ServerName,
                Score = m.Score,
                Kills = m.Kills,
                Deaths = m.Deaths,
                Ping = m.Ping,
                TeamName = m.TeamName,
                MapName = m.MapName,
                GameType = m.GameType,
                IsBot = m.IsBot ? 1 : 0,
                Game = m.Game
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_metrics (timestamp, server_guid, player_name, server_name, score, kills, deaths, ping, team_name, map_name, game_type, is_bot, game) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;
            await ExecuteCommandAsync(fullRequest);

            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Successfully stored {metrics.Count} player metrics to ClickHouse");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to store player metrics to ClickHouse: {ex.Message}");
        }
    }
}
