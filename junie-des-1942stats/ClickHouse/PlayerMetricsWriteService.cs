using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.ClickHouse;

public class PlayerMetricsWriteService : BaseClickHouseService, IClickHouseWriter
{
    public PlayerMetricsWriteService(HttpClient httpClient, string clickHouseUrl)
        : base(httpClient, clickHouseUrl)
    {
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
        var createTableQuery = @"
CREATE TABLE IF NOT EXISTS player_metrics (
    timestamp DateTime,
    server_guid String,
    server_name String,
    player_name String,
    score Int32,
    kills UInt16,
    deaths UInt16,
    ping UInt16,
    team_name String,
    map_name String,
    game_type String,
    is_bot UInt8
) ENGINE = MergeTree()
ORDER BY (server_guid, timestamp)
PARTITION BY toYYYYMM(timestamp)";

        await ExecuteCommandAsync(createTableQuery);
        
        // Add the is_bot column if it doesn't exist (for existing tables)
        await ExecuteCommandAsync("ALTER TABLE player_metrics ADD COLUMN IF NOT EXISTS is_bot UInt8 DEFAULT 0");
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

        await ExecuteCommandAsync(createViewQuery);
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
) ENGINE = MergeTree()
ORDER BY (server_guid, timestamp)
PARTITION BY toYYYYMM(timestamp)";

        await ExecuteCommandAsync(createTableQuery);
    }

    /// <summary>
    /// Migrates existing player_metrics data to server_online_counts table
    /// Uses SQLite GameServer data to populate the game column
    /// </summary>
    public async Task MigrateToServerOnlineCountsAsync(PlayerTrackerDbContext dbContext)
    {
        try
        {
            // First ensure the table exists
            await CreateServerOnlineCountsTableAsync();

            // Get server GUID to game mapping from SQLite
            var serverGameMapping = await dbContext.Servers
                .Where(s => !string.IsNullOrEmpty(s.Game))
                .ToDictionaryAsync(s => s.Guid, s => s.Game);

            if (!serverGameMapping.Any())
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] No servers with game data found in SQLite");
                return;
            }

            // Check what data already exists in server_online_counts
            DateTime? maxExistingTimestamp = null;
            try
            {
                var maxTimestampQuery = "SELECT max(timestamp) as max_ts FROM server_online_counts";
                var maxTimestampResult = await ExecuteQueryInternalAsync(maxTimestampQuery);
                
                // Parse ClickHouse result - it returns the timestamp as a string
                if (!string.IsNullOrWhiteSpace(maxTimestampResult))
                {
                    var lines = maxTimestampResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]) && lines[0] != "0000-00-00 00:00:00")
                    {
                        if (DateTime.TryParse(lines[0], out var parsedTimestamp))
                        {
                            maxExistingTimestamp = parsedTimestamp;
                            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Found existing data up to: {maxExistingTimestamp}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Table might not exist yet, or be empty - that's fine
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Could not check existing data (table may be empty): {ex.Message}");
            }

            // Build WHERE clause to exclude bots and optionally filter by timestamp
            var whereConditions = new List<string> { "is_bot = 0" };
            if (maxExistingTimestamp.HasValue)
            {
                whereConditions.Add($"timestamp > '{maxExistingTimestamp:yyyy-MM-dd HH:mm:ss}'");
            }
            var whereClause = $"WHERE {string.Join(" AND ", whereConditions)}";

            // Build the migration query with dynamic server mapping
            var serverMappingCases = string.Join(",\n        ", 
                serverGameMapping.Select(kvp => $"server_guid = '{kvp.Key}', '{kvp.Value}'"));

            var migrationQuery = $@"
INSERT INTO server_online_counts
SELECT 
    timestamp,
    server_guid,
    server_name,
    count(DISTINCT player_name) as players_online,
    any(map_name) as map_name,
    multiIf(
        {serverMappingCases},
        ''
    ) as game
FROM player_metrics
{whereClause}
GROUP BY timestamp, server_guid, server_name
ORDER BY timestamp, server_guid";

            await ExecuteCommandAsync(migrationQuery);
            
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Successfully migrated player_metrics to server_online_counts (excluding bots)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to migrate to server_online_counts: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Updates existing player_metrics records to set is_bot = 1 where ping = 0
    /// This is a one-time migration for historical data
    /// </summary>
    public async Task UpdateBotFlagsForExistingRecordsAsync()
    {
        try
        {
            var updateQuery = @"
ALTER TABLE player_metrics 
UPDATE is_bot = 1 
WHERE ping = 0 AND is_bot = 0";

            await ExecuteCommandAsync(updateQuery);
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Successfully updated bot flags for existing records where ping = 0");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to update bot flags for existing records: {ex.Message}");
            throw;
        }
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }

    public async Task StoreBatchedPlayerMetricsAsync(IEnumerable<IGameServer> servers, DateTime timestamp)
    {
        var allMetrics = new List<PlayerMetric>();

        foreach (var server in servers)
        {
            if (!server.Players.Any())
                continue;

            var serverMetrics = server.Players.Select(player =>
            {
                // Get team label from Teams array if TeamLabel is empty
                var teamLabel = player.TeamLabel;
                if (string.IsNullOrEmpty(teamLabel) && server.Teams?.Any() == true)
                {
                    var team = server.Teams.FirstOrDefault(t => t.Index == player.Team);
                    teamLabel = team?.Label ?? "";
                }

                var metric = new PlayerMetric
                {
                    Timestamp = timestamp,
                    ServerGuid = server.Guid,
                    ServerName = server.Name,
                    PlayerName = player.Name,
                    Score = player.Score,
                    Kills = (ushort)Math.Max(0, player.Kills),
                    Deaths = (ushort)Math.Max(0, player.Deaths),
                    Ping = (ushort)Math.Max(0, player.Ping),
                    TeamName = teamLabel,
                    MapName = server.MapName,
                    GameType = server.GameType,
                    IsBot = player.AiBot
                };
                return metric;
            });

            allMetrics.AddRange(serverMetrics);
        }

        if (allMetrics.Any())
        {
            await InsertPlayerMetricsAsync(allMetrics);
        }
    }

    /// <summary>
    /// Stores server online counts alongside the detailed player metrics
    /// </summary>
    public async Task StoreServerOnlineCountsAsync(IEnumerable<IGameServer> servers, DateTime timestamp, PlayerTrackerDbContext dbContext)
    {
        var onlineCounts = new List<ServerOnlineCount>();

        // Get server GUID to game mapping from SQLite
        var serverGuids = servers.Select(s => s.Guid).ToList();
        var serverGameMapping = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid) && !string.IsNullOrEmpty(s.Game))
            .ToDictionaryAsync(s => s.Guid, s => s.Game);

        foreach (var server in servers)
        {
            var playersOnline = (ushort)server.Players.Count(p => !p.AiBot);
            var game = serverGameMapping.GetValueOrDefault(server.Guid, "");

            var onlineCount = new ServerOnlineCount
            {
                Timestamp = timestamp,
                ServerGuid = server.Guid,
                ServerName = server.Name,
                PlayersOnline = playersOnline,
                MapName = server.MapName ?? "",
                Game = game
            };

            onlineCounts.Add(onlineCount);
        }

        if (onlineCounts.Any())
        {
            await InsertServerOnlineCountsAsync(onlineCounts);
        }
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

            csvWriter.WriteRecords(onlineCounts.Select(c => new
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

            // Write CSV records without header
            csvWriter.WriteRecords(metrics.Select(m => new
            {
                Timestamp = m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ServerGuid = m.ServerGuid,
                ServerName = m.ServerName,
                PlayerName = m.PlayerName,
                Score = m.Score,
                Kills = m.Kills,
                Deaths = m.Deaths,
                Ping = m.Ping,
                TeamName = m.TeamName,
                MapName = m.MapName,
                GameType = m.GameType,
                IsBot = m.IsBot ? 1 : 0
            }));

            var csvData = stringWriter.ToString();
            var query = $"INSERT INTO player_metrics (timestamp, server_guid, server_name, player_name, score, kills, deaths, ping, team_name, map_name, game_type, is_bot) FORMAT CSV";
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

public class PlayerMetric
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public ushort Ping { get; set; }
    public string TeamName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public bool IsBot { get; set; }
}

public class ServerOnlineCount
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public ushort PlayersOnline { get; set; }
    public string MapName { get; set; } = "";
    public string Game { get; set; } = "";
}
