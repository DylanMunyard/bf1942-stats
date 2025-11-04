using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Readers;
using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

/// <summary>
/// Service for comparing players and finding similar players.
/// Optimized to use post-processing for server GUID to name conversion
/// to avoid multiple individual database queries during player comparison operations.
/// </summary>
public class PlayerComparisonService : IPlayerComparisonService
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<PlayerComparisonService> _logger;
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly ICacheKeyService _cacheKeyService;
    private readonly PlayerInsightsService _playerInsightsService;

    public PlayerComparisonService(
        ClickHouseConnection connection,
        ILogger<PlayerComparisonService> logger,
        PlayerTrackerDbContext dbContext,
        ICacheService cacheService,
        ICacheKeyService cacheKeyService,
        PlayerInsightsService playerInsightsService)
    {
        _connection = connection;
        _logger = logger;
        _dbContext = dbContext;
        _cacheService = cacheService;
        _cacheKeyService = cacheKeyService;
        _playerInsightsService = playerInsightsService;
    }

    public async Task<PlayerComparisonResult> ComparePlayersAsync(string player1, string player2, string? serverGuid = null)
    {
        // Check cache first
        var cacheKey = _cacheKeyService.GetPlayerComparisonKey(player1, player2, serverGuid);
        var cachedResult = await _cacheService.GetAsync<PlayerComparisonResult>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for player comparison: {Player1} vs {Player2}", player1, player2);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for player comparison: {Player1} vs {Player2}", player1, player2);

        var result = new PlayerComparisonResult
        {
            Player1 = player1,
            Player2 = player2
        };

        // Collect all server GUIDs we'll need during processing
        var serverGuidsToConvert = new HashSet<string>();

        // If serverGuid is provided, add it to our collection
        if (!string.IsNullOrEmpty(serverGuid))
        {
            serverGuidsToConvert.Add(serverGuid);
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

        // 5. Overlapping Sessions (Head-to-Head) - collect server GUIDs
        var headToHeadData = await GetHeadToHeadData(player1, player2, serverGuid);
        serverGuidsToConvert.UnionWith(headToHeadData.serverGuids);

        // 6. Common Servers (servers where both players have played) - collect server GUIDs
        var commonServersData = await GetCommonServersData(player1, player2);
        serverGuidsToConvert.UnionWith(commonServersData.serverGuids);

        // 7. Kill Milestones for both players
        var killMilestones = await _playerInsightsService.GetPlayersKillMilestonesAsync(new List<string> { player1, player2 });
        result.Player1KillMilestones = killMilestones.Where(m => m.PlayerName == player1).Select(m => new KillMilestone
        {
            Milestone = m.Milestone,
            AchievedDate = m.AchievedDate,
            TotalKillsAtMilestone = m.TotalKillsAtMilestone,
            DaysToAchieve = m.DaysToAchieve
        }).ToList();
        result.Player2KillMilestones = killMilestones.Where(m => m.PlayerName == player2).Select(m => new KillMilestone
        {
            Milestone = m.Milestone,
            AchievedDate = m.AchievedDate,
            TotalKillsAtMilestone = m.TotalKillsAtMilestone,
            DaysToAchieve = m.DaysToAchieve
        }).ToList();

        // 8. Milestone Achievements for both players (excluding kill streaks)
        result.Player1MilestoneAchievements = await GetPlayerMilestoneAchievements(player1);
        result.Player2MilestoneAchievements = await GetPlayerMilestoneAchievements(player2);

        // POST-PROCESSING: Convert all collected server GUIDs to names in a single query
        var serverGuidToNameMapping = await GetServerGuidToNameMappingAsync(serverGuidsToConvert.ToList());

        // Apply the mapping to our results
        result.HeadToHead = ConvertHeadToHeadData(headToHeadData.sessions, serverGuidToNameMapping);
        result.CommonServers = ConvertCommonServersData(commonServersData.servers, serverGuidToNameMapping);

        // If serverGuid was provided, look up server details
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

        // Cache the result for 45 minutes
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(45));
        _logger.LogDebug("Cached player comparison result: {Player1} vs {Player2}", player1, player2);

        return result;
    }

    private async Task EnsureConnectionOpenAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
    }

    private async Task<List<KillRateComparison>> GetKillRates(string player1, string player2, string? serverGuid = null)
    {
        // Calculate kill rate (kills per minute) using player_rounds data
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
SELECT player_name, 
    SUM(final_kills) / nullIf(SUM(play_time_minutes), 0) AS kill_rate
FROM player_rounds
WHERE player_name IN ({Quote(player1)}, {Quote(player2)}){serverFilter}
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

    private async Task<List<BucketTotalsComparison>> GetBucketTotals(string player1, string player2, string? serverGuid = null)
    {
        // Buckets: last 30 days, last 6 months, last year, all time
        var buckets = new[]
        {
            ("Last30Days", "round_start_time >= now() - INTERVAL 30 DAY"),
            ("Last6Months", "round_start_time >= now() - INTERVAL 6 MONTH"),
            ("LastYear", "round_start_time >= now() - INTERVAL 1 YEAR"),
            ("AllTime", "1=1")
        };
        var results = new List<BucketTotalsComparison>();
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";

        foreach (var (label, condition) in buckets)
        {
            var query = $@"
SELECT player_name, 
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name IN ({Quote(player1)}, {Quote(player2)}) AND {condition}{serverFilter}
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

    private async Task<List<PingComparison>> GetAveragePing(string player1, string player2, string? serverGuid = null)
    {
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";

        // Optimized ping query - get recent pings more efficiently
        var query = $@"
SELECT player_name, avg(ping) as avg_ping
FROM player_metrics 
WHERE player_name IN ({Quote(player1)}, {Quote(player2)}) 
  AND ping > 0 
  AND ping < 1000  -- Filter out unrealistic ping values
  AND timestamp >= now() - INTERVAL 7 DAY  -- Only recent data for more relevant ping
{serverFilter}
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

    private async Task<List<MapPerformanceComparison>> GetMapPerformance(string player1, string player2, string? serverGuid = null)
    {
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
SELECT map_name, player_name, 
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name IN ({Quote(player1)}, {Quote(player2)}){serverFilter}
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

    private async Task<(List<HeadToHeadSession> sessions, HashSet<string> serverGuids)> GetHeadToHeadData(string player1, string player2, string? serverGuid = null)
    {
        // Find overlapping rounds using the player_rounds table
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND p1.server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
SELECT p1.round_start_time, p1.round_end_time, p1.server_guid, p1.map_name,
       p1.final_score, p1.final_kills, p1.final_deaths,
       p2.final_score, p2.final_kills, p2.final_deaths,
       p2.round_start_time, p2.round_end_time, p1.round_id
FROM player_rounds p1
JOIN player_rounds p2 ON p1.server_guid = p2.server_guid 
    AND p1.map_name = p2.map_name
    AND p1.round_start_time <= p2.round_end_time 
    AND p2.round_start_time <= p1.round_end_time
WHERE p1.player_name = {Quote(player1)} AND p2.player_name = {Quote(player2)}{serverFilter}
ORDER BY p1.round_start_time DESC
LIMIT 50";

        var sessions = new List<HeadToHeadSession>();
        var serverGuids = new HashSet<string>();
        var sessionData = new List<(DateTime, string, string, int?, int?, int?, int?, int?, int?, DateTime?, string?)>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var currentServerGuid = reader.GetString(2);
            serverGuids.Add(currentServerGuid);

            sessionData.Add((
                reader.GetDateTime(0),
                currentServerGuid,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)),
                reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
                reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
                reader.IsDBNull(8) ? null : Convert.ToInt32(reader.GetValue(8)),
                reader.IsDBNull(9) ? null : Convert.ToInt32(reader.GetValue(9)),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.IsDBNull(12) ? null : reader.GetString(12) // round_id
            ));
        }

        // Create sessions with server GUIDs (will be converted to names later)
        foreach (var data in sessionData)
        {
            sessions.Add(new HeadToHeadSession
            {
                Timestamp = data.Item1,
                ServerName = data.Item2, // This will be the GUID, converted later
                MapName = data.Item3,
                Player1Score = data.Item4 ?? 0,
                Player1Kills = data.Item5 ?? 0,
                Player1Deaths = data.Item6 ?? 0,
                Player2Score = data.Item7 ?? 0,
                Player2Kills = data.Item8 ?? 0,
                Player2Deaths = data.Item9 ?? 0,
                Player2Timestamp = data.Item10 ?? DateTime.MinValue,
                RoundId = data.Item11 // Round ID for UI linking
            });
        }

        return (sessions, serverGuids);
    }


    private async Task<(List<ServerDetails> servers, HashSet<string> serverGuids)> GetCommonServersData(string player1, string player2)
    {
        // Get servers where both players have played in the last 6 months
        var query = $@"
SELECT DISTINCT server_guid
FROM player_rounds
WHERE player_name = {Quote(player1)} AND round_start_time >= now() - INTERVAL 6 MONTH
INTERSECT
SELECT DISTINCT server_guid
FROM player_rounds
WHERE player_name = {Quote(player2)} AND round_start_time >= now() - INTERVAL 6 MONTH";

        var serverGuids = new HashSet<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            serverGuids.Add(reader.GetString(0));
        }

        // Create server details with GUIDs (will be converted to names later)
        var servers = new List<ServerDetails>();
        foreach (var guid in serverGuids)
        {
            servers.Add(new ServerDetails
            {
                Guid = guid,
                Name = guid, // This will be converted to the actual name later
                Ip = "",
                Port = 0,
                GameId = "",
                Country = null,
                Region = null,
                City = null,
                Timezone = null,
                Org = null
            });
        }

        return (servers, serverGuids);
    }

    public async Task<PlayerActivityHoursComparison> ComparePlayersActivityHoursAsync(string player1, string player2)
    {
        await EnsureConnectionOpenAsync();

        var result = new PlayerActivityHoursComparison
        {
            Player1 = player1,
            Player2 = player2
        };

        // Get common servers for more focused comparison
        var player1Servers = await GetPlayerActiveServers(player1);
        var player2Servers = await GetPlayerActiveServers(player2);
        var commonServers = player1Servers.Intersect(player2Servers).ToList();

        // Get activity hours for both players, scoped to common servers if they exist
        result.Player1ActivityHours = await GetPlayerActivityHours(player1, commonServers.Any() ? commonServers : player1Servers);
        result.Player2ActivityHours = await GetPlayerActivityHours(player2, commonServers.Any() ? commonServers : player2Servers);

        return result;
    }

    private async Task<List<HourlyActivity>> GetPlayerActivityHours(string playerName, List<string>? serverGuids = null)
    {
        var serverFilter = "";
        if (serverGuids != null && serverGuids.Any())
        {
            var serverList = string.Join(", ", serverGuids.Select(Quote));
            serverFilter = $" AND server_guid IN ({serverList})";
        }

        // Use the same approach as PlayerStatsService but with ClickHouse player_rounds data
        var query = $@"
SELECT 
    toHour(round_start_time) as hour_of_day,
    SUM(play_time_minutes) as total_minutes
FROM player_rounds
WHERE player_name = {Quote(playerName)} 
  AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
GROUP BY hour_of_day
ORDER BY hour_of_day";

        var hourlyActivity = new List<HourlyActivity>();

        // Initialize all hours with 0 minutes
        for (int hour = 0; hour < 24; hour++)
        {
            hourlyActivity.Add(new HourlyActivity { Hour = hour, MinutesActive = 0 });
        }

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var hour = Convert.ToInt32(reader.GetValue(0));
            var minutes = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));

            // Update the existing hour entry
            var hourEntry = hourlyActivity.FirstOrDefault(h => h.Hour == hour);
            if (hourEntry != null)
            {
                hourEntry.MinutesActive = minutes;
            }
        }

        return hourlyActivity.OrderByDescending(ha => ha.MinutesActive).ToList();
    }

    public async Task<SimilarPlayersResult> FindSimilarPlayersAsync(string targetPlayer, int limit = 10, bool filterBySimilarOnlineTime = true, SimilarityMode mode = SimilarityMode.Default)
    {
        await EnsureConnectionOpenAsync();

        var result = new SimilarPlayersResult
        {
            TargetPlayer = targetPlayer,
            SimilarPlayers = new List<SimilarPlayer>()
        };

        // Collect all server GUIDs we'll need during processing
        var serverGuidsToConvert = new HashSet<string>();

        // First, get the target player's stats to compare against
        var (targetStats, targetServerGuids) = await GetPlayerStatsForSimilarityWithGuids(targetPlayer);
        if (targetStats == null)
        {
            _logger.LogWarning("Target player {PlayerName} not found", targetPlayer);
            return result;
        }

        serverGuidsToConvert.UnionWith(targetServerGuids);
        result.TargetPlayerStats = targetStats;

        // Find similar players based on multiple criteria
        var (similarPlayers, candidateServerGuids) = await FindPlayersBySimilarityWithGuids(targetPlayer, targetStats, limit * 3, null, mode); // Get more candidates
        serverGuidsToConvert.UnionWith(candidateServerGuids);

        // POST-PROCESSING: Convert all collected server GUIDs to names in a single query
        var serverGuidToNameMapping = await GetServerGuidToNameMappingAsync(serverGuidsToConvert.ToList());

        // Apply the mapping to target stats
        ApplyServerGuidToNameMapping(targetStats, serverGuidToNameMapping);

        // Apply the mapping to similar players
        foreach (var player in similarPlayers)
        {
            ApplyServerGuidToNameMapping(player, serverGuidToNameMapping);
        }

        // OPTIMIZATION: Bulk calculate temporal overlaps for all candidates at once
        Dictionary<string, double> bulkTemporalOverlaps = new();
        if (similarPlayers.Any())
        {
            var candidateNames = similarPlayers.Select(p => p.PlayerName).ToList();
            bulkTemporalOverlaps = await CalculateBulkTemporalOverlap(targetPlayer, candidateNames);
        }

        // Calculate similarity scores and rank them - no minimum threshold, let users see all results
        var rankedPlayers = new List<SimilarPlayer>();

        foreach (var candidate in similarPlayers)
        {
            // Use pre-calculated temporal overlap instead of individual query
            var temporalOverlapMinutes = bulkTemporalOverlaps.GetValueOrDefault(candidate.PlayerName, 0.0);
            var similarPlayer = CalculateSimilarityScore(targetStats, candidate, mode, temporalOverlapMinutes);
            // Always add the player - let the score speak for itself
            rankedPlayers.Add(similarPlayer);
        }

        rankedPlayers = rankedPlayers
            .OrderByDescending(p => p.SimilarityScore)
            .Take(limit)
            .ToList();

        result.SimilarPlayers = rankedPlayers;

        return result;
    }

    private async Task<(PlayerSimilarityStats? stats, HashSet<string> serverGuids)> GetPlayerStatsForSimilarityWithGuids(string playerName)
    {
        var query = $@"
WITH total_stats AS (
    SELECT 
        SUM(final_kills) AS total_kills,
        SUM(final_deaths) AS total_deaths,
        SUM(play_time_minutes) AS total_play_time_minutes
    FROM player_rounds
    WHERE player_name = {Quote(playerName)} AND round_start_time >= now() - INTERVAL 6 MONTH
),
server_playtime AS (
    SELECT server_guid, SUM(play_time_minutes) AS total_minutes
    FROM player_rounds
    WHERE player_name = {Quote(playerName)} AND round_start_time >= now() - INTERVAL 6 MONTH
    GROUP BY server_guid
    ORDER BY total_minutes DESC
    LIMIT 1
),
game_ids AS (
    SELECT DISTINCT game_id
    FROM player_rounds
    WHERE player_name = {Quote(playerName)} AND round_start_time >= now() - INTERVAL 6 MONTH
)
SELECT 
    t.total_kills,
    t.total_deaths,
    t.total_play_time_minutes,
    s.server_guid as favorite_server_guid,
    s.total_minutes as favorite_server_minutes,
    arrayStringConcat(groupArray(g.game_id), ',') as game_ids
FROM total_stats t
CROSS JOIN server_playtime s
CROSS JOIN game_ids g
GROUP BY t.total_kills, t.total_deaths, t.total_play_time_minutes, s.server_guid, s.total_minutes";

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var totalKills = reader.IsDBNull(0) ? 0u : Convert.ToUInt32(reader.GetValue(0));
            var totalDeaths = reader.IsDBNull(1) ? 0u : Convert.ToUInt32(reader.GetValue(1));
            var totalPlayTime = reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetValue(2));
            var favoriteServerGuid = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var favoriteServerMinutes = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetValue(4));
            var gameIds = reader.IsDBNull(5) ? "" : reader.GetString(5);

            // Collect server GUIDs (will be converted to names later)
            var serverGuidsToCollect = new HashSet<string>();
            if (!string.IsNullOrEmpty(favoriteServerGuid))
            {
                serverGuidsToCollect.Add(favoriteServerGuid);
            }

            var playerStats = new PlayerSimilarityStats
            {
                PlayerName = playerName,
                TotalKills = totalKills,
                TotalDeaths = totalDeaths,
                TotalPlayTimeMinutes = totalPlayTime,
                KillDeathRatio = totalDeaths > 0 ? (double)totalKills / totalDeaths : totalKills,
                FavoriteServerName = favoriteServerGuid, // This will be converted to name later
                FavoriteServerPlayTimeMinutes = favoriteServerMinutes,
                GameIds = gameIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            };

            // CRITICAL: Get target player's active servers for scoped queries
            var activeServers = await GetPlayerActiveServers(playerName);
            serverGuidsToCollect.UnionWith(activeServers);

            // Fetch target player's additional data needed for similarity comparison
            var serverFilter = "";
            if (activeServers.Any())
            {
                var serverList = string.Join(", ", activeServers.Select(Quote));
                serverFilter = $" AND server_guid IN ({serverList})";
            }

            // Get typical online hours for target player
            playerStats.TypicalOnlineHours = await GetPlayerTypicalOnlineHours(playerName, activeServers);

            // Get server pings for target player
            var (targetPings, pingServerGuids) = await GetPlayerServerPingsWithGuids(playerName, activeServers);
            playerStats.ServerPings = targetPings;
            serverGuidsToCollect.UnionWith(pingServerGuids);

            // Get map dominance for target player
            playerStats.MapDominanceScores = await GetPlayerMapDominanceScores(playerName, activeServers);

            return (playerStats, serverGuidsToCollect);
        }

        return (null, new HashSet<string>());
    }

    private async Task<List<string>> GetPlayerActiveServers(string playerName)
    {
        var query = $@"
SELECT DISTINCT server_guid
FROM player_rounds
WHERE player_name = {Quote(playerName)} 
  AND round_start_time >= now() - INTERVAL 6 MONTH
  AND play_time_minutes > 5";

        var serverGuids = new List<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            serverGuids.Add(reader.GetString(0));
        }

        return serverGuids;
    }

    private async Task<List<int>> GetPlayerTypicalOnlineHours(string playerName, List<string>? serverGuids = null)
    {
        var serverFilter = "";
        if (serverGuids != null && serverGuids.Any())
        {
            var serverList = string.Join(", ", serverGuids.Select(Quote));
            serverFilter = $" AND server_guid IN ({serverList})";
        }

        var query = $@"
WITH hourly_playtime AS (
    SELECT
        toHour(round_start_time) as hour_of_day,
        SUM(play_time_minutes) as total_minutes
    FROM player_rounds
    WHERE player_name = {Quote(playerName)}
      AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
    GROUP BY hour_of_day
),
percentiles AS (
    SELECT
        quantile(0.95)(total_minutes) as p95_minutes
    FROM hourly_playtime
)
SELECT hour_of_day
FROM hourly_playtime, percentiles
WHERE total_minutes >= p95_minutes * 0.5  -- Include hours with at least 50% of p95 activity
ORDER BY hour_of_day";

        var activeHours = new List<int>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            activeHours.Add(Convert.ToInt32(reader.GetValue(0)));
        }

        return activeHours;
    }

    private async Task<(Dictionary<string, double> pings, HashSet<string> serverGuids)> GetPlayerServerPingsWithGuids(string playerName, List<string>? serverGuids = null)
    {
        var serverFilter = "";
        if (serverGuids != null && serverGuids.Any())
        {
            var serverList = string.Join(", ", serverGuids.Select(Quote));
            serverFilter = $" AND server_guid IN ({serverList})";
        }

        var query = $@"
SELECT
    server_guid,
    avg(ping) as avg_ping
FROM player_metrics
WHERE player_name = {Quote(playerName)}
  AND ping > 0
  AND ping < 1000{serverFilter}
  AND timestamp >= now() - INTERVAL 30 DAY  -- Recent ping data for accuracy
GROUP BY server_guid
HAVING count(*) >= 10  -- Require at least 10 measurements for reliability";

        var serverPingsWithGuids = new Dictionary<string, double>();
        var collectedServerGuids = new HashSet<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var serverGuid = reader.GetString(0);
            var avgPing = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1));
            serverPingsWithGuids[serverGuid] = avgPing; // Use GUID as key for now
            collectedServerGuids.Add(serverGuid);
        }

        return (serverPingsWithGuids, collectedServerGuids);
    }

    private async Task<Dictionary<string, double>> GetPlayerMapDominanceScores(string playerName, List<string>? serverGuids = null)
    {
        // Calculate dominance as the ratio of player's performance vs average performance on each map
        // Only compare against averages from servers where the target player actually plays
        var serverFilter = "";
        if (serverGuids != null && serverGuids.Any())
        {
            var serverList = string.Join(", ", serverGuids.Select(Quote));
            serverFilter = $" AND server_guid IN ({serverList})";
        }

        var query = $@"
WITH player_map_stats AS (
    SELECT
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as player_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as player_score_rate,
        SUM(play_time_minutes) as total_play_time
    FROM player_rounds
    WHERE player_name = {Quote(playerName)}
      AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5{serverFilter}
    GROUP BY map_name
    HAVING total_play_time >= 60  -- At least 1 hour on the map
),
map_averages AS (
    SELECT
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as avg_score_rate
    FROM player_rounds
    WHERE is_bot = 0 AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5{serverFilter}
    GROUP BY map_name
)
SELECT
    p.map_name,
    CASE
        WHEN a.avg_kill_rate > 0 AND a.avg_score_rate > 0 THEN
            (p.player_kill_rate / a.avg_kill_rate + p.player_score_rate / a.avg_score_rate) / 2
        ELSE 1.0
    END as dominance_score
FROM player_map_stats p
JOIN map_averages a ON p.map_name = a.map_name";

        var mapDominance = new Dictionary<string, double>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var mapName = reader.GetString(0);
            var dominanceScore = reader.IsDBNull(1) ? 1.0 : Convert.ToDouble(reader.GetValue(1));
            mapDominance[mapName] = dominanceScore;
        }

        return mapDominance;
    }

    private async Task<Dictionary<string, string>> GetServerGuidToNameMappingAsync(List<string> serverGuids)
    {
        return await _dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);
    }

    private async Task<(List<PlayerSimilarityStats> players, HashSet<string> serverGuids)> FindPlayersBySimilarityWithGuids(string targetPlayer, PlayerSimilarityStats targetStats, int limit, List<int>? targetOnlineHours = null, SimilarityMode mode = SimilarityMode.Default)
    {
        // Get target player's active servers to scope the comparison
        var targetActiveServers = await GetPlayerActiveServers(targetPlayer);
        if (!targetActiveServers.Any())
        {
            return (new List<PlayerSimilarityStats>(), new HashSet<string>()); // No servers to compare against
        }

        // Use very relaxed filtering - just require minimum data for reliable comparison
        // Let the similarity scoring algorithm do the ranking instead of excluding candidates
        string playTimeFilter = "total_play_time_minutes >= 30"; // At least 30 minutes of data for any mode

        // Create server filter - only include players who have played on the same servers as target player
        var serverList = string.Join(", ", targetActiveServers.Select(Quote));
        var serverFilter = $" AND server_guid IN ({serverList})";

        // OPTIMIZATION: Build query based on mode to skip expensive calculations when not needed
        var includePingData = mode == SimilarityMode.AliasDetection;
        var includeMapDominance = mode == SimilarityMode.AliasDetection;

        // OPTIMIZATION: Single comprehensive query to get all candidate data at once
        var bulkQuery = $@"
WITH player_stats AS (
    SELECT 
        player_name,
        SUM(final_kills) AS total_kills,
        SUM(final_deaths) AS total_deaths,
        SUM(play_time_minutes) AS total_play_time_minutes,
        CASE WHEN SUM(final_deaths) > 0 THEN SUM(final_kills) / SUM(final_deaths) ELSE toFloat64(SUM(final_kills)) END AS kdr
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
    GROUP BY player_name
    HAVING {playTimeFilter}
),
server_playtime AS (
    SELECT player_name, server_guid, SUM(play_time_minutes) AS total_minutes
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
    GROUP BY player_name, server_guid
),
favorite_servers AS (
    SELECT player_name, server_guid, total_minutes,
           ROW_NUMBER() OVER (PARTITION BY player_name ORDER BY total_minutes DESC) as rn
    FROM server_playtime
),
player_game_ids AS (
    SELECT player_name, arrayStringConcat(groupArray(DISTINCT game_id), ',') as game_ids
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
    GROUP BY player_name
),
-- Get all active servers for all candidates in one query
candidate_active_servers AS (
    SELECT 
        player_name,
        groupArray(DISTINCT server_guid) as active_servers
    FROM player_rounds
    WHERE player_name IN (SELECT player_name FROM player_stats)
      AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5
    GROUP BY player_name
),
-- Calculate threshold for each player first
candidate_hour_thresholds AS (
    SELECT
        player_name,
        quantile(0.95)(total_minutes) * 0.5 as threshold
    FROM (
        SELECT
            player_name,
            toHour(round_start_time) as hour_of_day,
            SUM(play_time_minutes) as total_minutes
        FROM player_rounds
        WHERE player_name IN (SELECT player_name FROM player_stats)
          AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
        GROUP BY player_name, hour_of_day
    )
    GROUP BY player_name
),
-- Get all typical online hours for all candidates in one query
candidate_online_hours AS (
    SELECT
        h.player_name,
        groupArray(h.hour_of_day) as typical_hours
    FROM (
        SELECT
            player_name,
            toHour(round_start_time) as hour_of_day,
            SUM(play_time_minutes) as total_minutes
        FROM player_rounds
        WHERE player_name IN (SELECT player_name FROM player_stats)
          AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
        GROUP BY player_name, hour_of_day
    ) h
    JOIN candidate_hour_thresholds t ON h.player_name = t.player_name
    WHERE h.total_minutes >= t.threshold
    GROUP BY h.player_name
){(includePingData ? $@",
-- Get all server pings for all candidates in one query
candidate_server_pings AS (
    SELECT
        player_name,
        groupArray(concat(server_guid, ':', toString(avg_ping))) as ping_data
    FROM (
        SELECT
            player_name,
            server_guid,
            avg(ping) as avg_ping
        FROM player_metrics
        WHERE player_name IN (SELECT player_name FROM player_stats)
          AND ping > 0
          AND ping < 1000{serverFilter}
          AND timestamp >= now() - INTERVAL 30 DAY
        GROUP BY player_name, server_guid
        HAVING count(*) >= 10
    )
    GROUP BY player_name
)" : "")}{(includeMapDominance ? $@",
-- Calculate map averages across all non-bot players for dominance comparison
map_averages AS (
    SELECT
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as avg_score_rate
    FROM player_rounds
    WHERE is_bot = 0
      AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5{serverFilter}
    GROUP BY map_name
),
-- Get map dominance scores for all candidates in one query
candidate_map_dominance AS (
    SELECT
        player_name,
        groupArray(concat(map_name, ':', toString(dominance_score))) as dominance_data
    FROM (
        SELECT
            p.player_name,
            p.map_name,
            CASE
                WHEN a.avg_kill_rate > 0 AND a.avg_score_rate > 0 THEN
                    (p.player_kill_rate / a.avg_kill_rate + p.player_score_rate / a.avg_score_rate) / 2
                ELSE 1.0
            END as dominance_score
        FROM (
            SELECT
                player_name,
                map_name,
                AVG(final_kills / nullIf(play_time_minutes, 0)) as player_kill_rate,
                AVG(final_score / nullIf(play_time_minutes, 0)) as player_score_rate,
                SUM(play_time_minutes) as total_play_time
            FROM player_rounds
            WHERE player_name IN (SELECT player_name FROM player_stats)
              AND round_start_time >= now() - INTERVAL 6 MONTH{serverFilter}
              AND play_time_minutes > 5
            GROUP BY player_name, map_name
            HAVING total_play_time >= 60
        ) p
        JOIN map_averages a ON p.map_name = a.map_name
    )
    GROUP BY player_name
)" : "")}
SELECT
    p.player_name,
    p.total_kills,
    p.total_deaths,
    p.total_play_time_minutes,
    p.kdr,
    f.server_guid as favorite_server_guid,
    f.total_minutes as favorite_server_minutes,
    g.game_ids,
    cas.active_servers,
    coh.typical_hours{(includePingData ? ",\n    csp.ping_data" : ",\n    '' as ping_data")}{(includeMapDominance ? ",\n    cmd.dominance_data" : ",\n    '' as dominance_data")}
FROM player_stats p
LEFT JOIN favorite_servers f ON p.player_name = f.player_name AND f.rn = 1
LEFT JOIN player_game_ids g ON p.player_name = g.player_name
LEFT JOIN candidate_active_servers cas ON p.player_name = cas.player_name
LEFT JOIN candidate_online_hours coh ON p.player_name = coh.player_name{(includePingData ? "\nLEFT JOIN candidate_server_pings csp ON p.player_name = csp.player_name" : "")}{(includeMapDominance ? "\nLEFT JOIN candidate_map_dominance cmd ON p.player_name = cmd.player_name" : "")}
ORDER BY
    -- Prioritize players on same favorite server
    CASE WHEN f.server_guid = '{targetStats.FavoriteServerName}' THEN 0 ELSE 1 END,
    -- Then sort by combined similarity (KDR and play time)
    abs(p.kdr - {targetStats.KillDeathRatio}) +
    abs(p.total_play_time_minutes - {targetStats.TotalPlayTimeMinutes}) / 1000
LIMIT {limit * 5}";

        var players = new List<PlayerSimilarityStats>();
        var allServerGuids = new HashSet<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = bulkQuery;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var playerName = reader.GetString(0);
            var totalKills = reader.IsDBNull(1) ? 0u : Convert.ToUInt32(reader.GetValue(1));
            var totalDeaths = reader.IsDBNull(2) ? 0u : Convert.ToUInt32(reader.GetValue(2));
            var totalPlayTime = reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3));
            var kdr = reader.IsDBNull(4) ? 0.0 : Convert.ToDouble(reader.GetValue(4));
            var favoriteServerGuid = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var favoriteServerMinutes = reader.IsDBNull(6) ? 0.0 : Convert.ToDouble(reader.GetValue(6));
            var gameIds = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var activeServersStr = reader.IsDBNull(8) ? "" : reader.GetString(8);
            var typicalHoursStr = reader.IsDBNull(9) ? "" : reader.GetString(9);
            var pingDataStr = reader.IsDBNull(10) ? "" : reader.GetString(10);
            var dominanceDataStr = reader.IsDBNull(11) ? "" : reader.GetString(11);

            // Parse active servers - handle ClickHouse array format with potential spaces
            var activeServersArray = string.IsNullOrEmpty(activeServersStr) ?
                new string[0] : activeServersStr.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().Trim('\'', '"')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            // Parse typical hours - handle ClickHouse array format with potential spaces
            var typicalHoursArray = string.IsNullOrEmpty(typicalHoursStr) ?
                new string[0] : typicalHoursStr.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().Trim('\'', '"')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            // Parse ping data - handle ClickHouse array format with potential spaces
            var serverPings = new Dictionary<string, double>();
            if (!string.IsNullOrEmpty(pingDataStr))
            {
                var pingEntries = pingDataStr.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in pingEntries)
                {
                    var cleanEntry = entry.Trim().Trim('\'', '"');
                    var parts = cleanEntry.Split(':', 2);
                    if (parts.Length == 2 && double.TryParse(parts[1], out var ping))
                    {
                        serverPings[parts[0]] = ping;
                        allServerGuids.Add(parts[0]);
                    }
                }
            }

            // Parse map dominance data - handle ClickHouse array format with potential spaces
            var mapDominance = new Dictionary<string, double>();
            if (!string.IsNullOrEmpty(dominanceDataStr))
            {
                var domEntries = dominanceDataStr.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in domEntries)
                {
                    var cleanEntry = entry.Trim().Trim('\'', '"');
                    var parts = cleanEntry.Split(':', 2);
                    if (parts.Length == 2 && double.TryParse(parts[1], out var score))
                    {
                        mapDominance[parts[0]] = score;
                    }
                }
            }

            // Collect server GUIDs
            if (!string.IsNullOrEmpty(favoriteServerGuid))
            {
                allServerGuids.Add(favoriteServerGuid);
            }
            allServerGuids.UnionWith(activeServersArray);

            var playerStats = new PlayerSimilarityStats
            {
                PlayerName = playerName,
                TotalKills = totalKills,
                TotalDeaths = totalDeaths,
                TotalPlayTimeMinutes = totalPlayTime,
                KillDeathRatio = kdr,
                FavoriteServerName = favoriteServerGuid, // Will be converted to name later
                FavoriteServerPlayTimeMinutes = favoriteServerMinutes,
                GameIds = gameIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                TypicalOnlineHours = typicalHoursArray.Select(h => int.TryParse(h, out var hour) ? hour : 0)
                    .Where(h => h >= 0 && h <= 23).ToList(),
                ServerPings = serverPings,
                MapDominanceScores = mapDominance
            };

            // Calculate common servers with target (no additional queries needed)
            var candidateActiveServers = activeServersArray.ToList();
            var commonServers = candidateActiveServers.Intersect(targetActiveServers).ToList();

            // For alias detection mode, temporal non-overlap will be calculated in bulk later if needed
            if (mode == SimilarityMode.AliasDetection)
            {
                // Temporal non-overlap calculation moved to bulk processing
                playerStats.TemporalNonOverlapScore = 0.0; // Will be calculated later if needed
            }

            players.Add(playerStats);
        }

        return (players, allServerGuids);
    }

    // OPTIMIZATION: Bulk temporal overlap calculation for all candidates at once
    private async Task<Dictionary<string, double>> CalculateBulkTemporalOverlap(string targetPlayer, List<string> candidatePlayerNames)
    {
        if (!candidatePlayerNames.Any())
            return new Dictionary<string, double>();

        var candidateList = string.Join(", ", candidatePlayerNames.Select(Quote));

        var query = $@"
WITH target_sessions AS (
    SELECT 
        round_start_time,
        round_end_time,
        server_guid
    FROM player_rounds
    WHERE player_name = {Quote(targetPlayer)} 
      AND round_start_time >= now() - INTERVAL 3 MONTH
),
candidate_sessions AS (
    SELECT 
        player_name,
        round_start_time,
        round_end_time,
        server_guid
    FROM player_rounds
    WHERE player_name IN ({candidateList})
      AND round_start_time >= now() - INTERVAL 3 MONTH
),
overlapping_sessions AS (
    SELECT 
        c.player_name,
        SUM(
            CASE 
                WHEN c.server_guid = t.server_guid 
                     AND c.round_start_time < t.round_end_time 
                     AND c.round_end_time > t.round_start_time
                THEN dateDiff('minute', 
                    greatest(c.round_start_time, t.round_start_time),
                    least(c.round_end_time, t.round_end_time)
                )
                ELSE 0
            END
        ) as overlap_minutes
    FROM candidate_sessions c
    CROSS JOIN target_sessions t
    GROUP BY c.player_name
)
SELECT 
    player_name,
    overlap_minutes
FROM overlapping_sessions
WHERE overlap_minutes > 0
UNION ALL
SELECT 
    player_name,
    0 as overlap_minutes
FROM (SELECT DISTINCT player_name FROM candidate_sessions) cs
WHERE player_name NOT IN (SELECT player_name FROM overlapping_sessions)";

        var overlaps = new Dictionary<string, double>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var playerName = reader.GetString(0);
            var overlapMinutes = reader.IsDBNull(1) ? 0.0 : Convert.ToDouble(reader.GetValue(1));
            overlaps[playerName] = overlapMinutes;
        }

        // Ensure all candidates are included
        foreach (var candidate in candidatePlayerNames)
        {
            if (!overlaps.ContainsKey(candidate))
            {
                overlaps[candidate] = 0.0;
            }
        }

        return overlaps;
    }

    private SimilarPlayer CalculateSimilarityScore(PlayerSimilarityStats target, PlayerSimilarityStats candidate, SimilarityMode mode = SimilarityMode.Default, double? preCalculatedTemporalOverlap = null)
    {
        double score = 0;
        var reasons = new List<string>();

        // Determine if temporal similarity is available
        var hasTemporalData = target.TypicalOnlineHours.Any() && candidate.TypicalOnlineHours.Any();

        // For alias detection mode, note issues but don't exclude - let scoring handle it
        var temporalOverlapMinutes = preCalculatedTemporalOverlap ?? 0.0;
        if (mode == SimilarityMode.AliasDetection && temporalOverlapMinutes > 30.0)
        {
            reasons.Add($"High temporal overlap: {temporalOverlapMinutes:F0} minutes (suggests not an alias)");
        }

        // Check for high ping differences in alias mode
        if (mode == SimilarityMode.AliasDetection)
        {
            var commonServers = target.ServerPings.Keys.Intersect(candidate.ServerPings.Keys).ToList();
            if (commonServers.Any())
            {
                var maxPingDiff = commonServers.Max(server =>
                    Math.Abs(target.ServerPings[server] - candidate.ServerPings[server]));

                if (maxPingDiff > 30.0)
                {
                    reasons.Add($"High ping difference: {maxPingDiff:F0}ms (suggests different location)");
                }
            }
        }

        // Adjust weights based on similarity mode and temporal data availability
        double playTimeWeight, kdrWeight, serverWeight, temporalWeight, pingWeight, mapDominanceWeight,
               temporalNonOverlapWeight;

        if (mode == SimilarityMode.AliasDetection)
        {
            // For alias detection, prioritize ping similarity and non-overlapping play times
            playTimeWeight = 0.0;                           // REMOVED: Play time irrelevant for aliases
            kdrWeight = 0.30;                               // Skill consistency is important
            serverWeight = 0.25;                            // Server affinity important
            temporalWeight = 0.0;                           // REPLACED with non-overlap
            temporalNonOverlapWeight = 0.20;                // Never seen online together
            pingWeight = 0.20;                              // Similar ping on same servers
            mapDominanceWeight = 0.05;                      // Map performance patterns
        }
        else
        {
            // Default algorithm weights - focus on play style similarity
            // Make KDR and kill rate the primary factors
            playTimeWeight = 0.15;                          // Some similarity in play time
            kdrWeight = 0.40;                               // PRIMARY: Similar skill level (K/D ratio)
            serverWeight = 0.25;                            // Important: plays on same servers
            temporalWeight = hasTemporalData ? 0.20 : 0.0;  // Similar online times if available
            temporalNonOverlapWeight = 0.0;
            pingWeight = 0.0;
            mapDominanceWeight = 0.0;
        }

        // Play time similarity - more forgiving for wider ranges
        var playTimeDiff = Math.Abs(target.TotalPlayTimeMinutes - candidate.TotalPlayTimeMinutes);
        var playTimeRatio = Math.Max(target.TotalPlayTimeMinutes, candidate.TotalPlayTimeMinutes);
        var playTimeScore = playTimeRatio > 0 ? Math.Max(0, 1 - (playTimeDiff / playTimeRatio)) : 1;
        score += playTimeScore * playTimeWeight;

        if (playTimeScore > 0.6)
            reasons.Add($"Similar play time ({candidate.TotalPlayTimeMinutes:F0} vs {target.TotalPlayTimeMinutes:F0} minutes)");

        // KDR similarity - more forgiving for wider differences
        var kdrDiff = Math.Abs(target.KillDeathRatio - candidate.KillDeathRatio);
        // Use exponential decay for more forgiving scoring: score drops to 0.5 at diff=1, 0.25 at diff=2
        var kdrScore = Math.Max(0, Math.Pow(0.5, kdrDiff));
        score += kdrScore * kdrWeight;

        if (kdrScore > 0.5)
            reasons.Add($"Similar KDR ({candidate.KillDeathRatio:F2} vs {target.KillDeathRatio:F2})");

        // Kill rate similarity - add as bonus if available
        var targetKillRate = target.TotalPlayTimeMinutes > 0 ? target.TotalKills / target.TotalPlayTimeMinutes : 0;
        var candidateKillRate = candidate.TotalPlayTimeMinutes > 0 ? candidate.TotalKills / candidate.TotalPlayTimeMinutes : 0;
        if (targetKillRate > 0 && candidateKillRate > 0)
        {
            var killRateDiff = Math.Abs(targetKillRate - candidateKillRate);
            var killRateScore = Math.Max(0, Math.Pow(0.5, killRateDiff * 2)); // More strict on kill rate
            // Add as bonus to KDR weight (up to 20% boost)
            score += killRateScore * kdrWeight * 0.2;

            if (killRateScore > 0.5)
                reasons.Add($"Similar kill rate ({candidateKillRate:F2} vs {targetKillRate:F2} kills/min)");
        }

        // Server affinity
        var serverScore = target.FavoriteServerName == candidate.FavoriteServerName ? 1.0 : 0.0;
        score += serverScore * serverWeight;

        if (serverScore > 0)
            reasons.Add("Plays on same favorite server");

        // Temporal similarity (overlap in online hours) - DEFAULT MODE ONLY
        if (hasTemporalData && temporalWeight > 0)
        {
            var commonHours = target.TypicalOnlineHours.Intersect(candidate.TypicalOnlineHours).Count();
            var totalUniqueHours = target.TypicalOnlineHours.Union(candidate.TypicalOnlineHours).Count();
            var temporalScore = totalUniqueHours > 0 ? (double)commonHours / totalUniqueHours : 0;
            score += temporalScore * temporalWeight;

            if (temporalScore > 0.3) // Lower threshold for showing this reason
            {
                var overlapHours = target.TypicalOnlineHours.Intersect(candidate.TypicalOnlineHours).OrderBy(h => h).ToList();
                if (overlapHours.Any())
                {
                    var hoursText = string.Join(", ", overlapHours.Select(h => $"{h:D2}:00"));
                    reasons.Add($"Similar online times ({overlapHours.Count} overlapping hours: {hoursText})");
                }
            }
        }
        else if (!hasTemporalData && temporalWeight > 0)
        {
            // If temporal data is missing, redistribute the weight to other factors
            // This ensures players with missing data aren't unfairly penalized
            var redistributeWeight = temporalWeight / 2.0;
            score += redistributeWeight; // Give partial credit for missing data
        }

        // Temporal NON-overlap (for alias detection) - ALIAS MODE ONLY
        if (mode == SimilarityMode.AliasDetection && temporalNonOverlapWeight > 0)
        {
            var nonOverlapScore = candidate.TemporalNonOverlapScore;
            score += nonOverlapScore * temporalNonOverlapWeight;

            if (nonOverlapScore > 0.8)
            {
                reasons.Add($"Never seen online simultaneously (non-overlap score: {nonOverlapScore:F2})");
            }
        }

        // Ping similarity (for alias detection)
        if (mode == SimilarityMode.AliasDetection && pingWeight > 0)
        {
            var pingScore = CalculatePingSimilarity(target.ServerPings, candidate.ServerPings);
            score += pingScore * pingWeight;

            if (pingScore > 0.7)
            {
                var commonServers = target.ServerPings.Keys.Intersect(candidate.ServerPings.Keys).Count();
                reasons.Add($"Very similar ping patterns ({commonServers} common servers, score: {pingScore:F2})");
            }
        }

        // Map dominance similarity (for alias detection)
        if (mode == SimilarityMode.AliasDetection && mapDominanceWeight > 0)
        {
            var dominanceScore = CalculateMapDominanceSimilarity(target.MapDominanceScores, candidate.MapDominanceScores);
            score += dominanceScore * mapDominanceWeight;

            if (dominanceScore > 0.6)
            {
                var commonMaps = target.MapDominanceScores.Keys.Intersect(candidate.MapDominanceScores.Keys).Count();
                reasons.Add($"Similar map performance patterns ({commonMaps} common maps, score: {dominanceScore:F2})");
            }
        }

        // Session pattern similarity (for alias detection) - REMOVED for now
        // Focus on core alias detection factors: ping similarity, temporal non-overlap, KDR, and server affinity

        return new SimilarPlayer
        {
            PlayerName = candidate.PlayerName,
            TotalKills = candidate.TotalKills,
            TotalDeaths = candidate.TotalDeaths,
            TotalPlayTimeMinutes = candidate.TotalPlayTimeMinutes,
            KillDeathRatio = candidate.KillDeathRatio,
            KillsPerMinute = candidate.TotalPlayTimeMinutes > 0 ? candidate.TotalKills / candidate.TotalPlayTimeMinutes : 0,
            FavoriteServerName = candidate.FavoriteServerName,
            FavoriteServerPlayTimeMinutes = candidate.FavoriteServerPlayTimeMinutes,
            GameIds = candidate.GameIds,
            TypicalOnlineHours = candidate.TypicalOnlineHours,
            ServerPings = candidate.ServerPings,
            MapDominanceScores = candidate.MapDominanceScores,
            TemporalNonOverlapScore = candidate.TemporalNonOverlapScore,
            TemporalOverlapMinutes = preCalculatedTemporalOverlap ?? 0.0,
            SimilarityScore = score,
            SimilarityReasons = reasons
        };
    }

    private static double CalculatePingSimilarity(Dictionary<string, double> targetPings, Dictionary<string, double> candidatePings)
    {
        var commonServers = targetPings.Keys.Intersect(candidatePings.Keys).ToList();

        if (!commonServers.Any())
            return 0.0; // No common servers

        double totalSimilarity = 0;
        int validComparisons = 0;

        foreach (var server in commonServers)
        {
            var targetPing = targetPings[server];
            var candidatePing = candidatePings[server];

            // Calculate similarity based on ping difference - STRICTER for alias detection
            var pingDiff = Math.Abs(targetPing - candidatePing);

            // For aliases, ping should be nearly identical (within 1-2ms for same physical location)
            var similarity = 0.0;
            if (pingDiff <= 1.0)
                similarity = 1.0; // Perfect match - almost certainly same location
            else if (pingDiff <= 2.0)
                similarity = 0.9; // Very likely same location
            else if (pingDiff <= 3.0)
                similarity = 0.7; // Possibly same location
            else if (pingDiff <= 5.0)
                similarity = 0.4; // Less likely but possible
            else if (pingDiff <= 10.0)
                similarity = 0.1; // Unlikely to be same location
                                  // else similarity remains 0.0

            totalSimilarity += similarity;
            validComparisons++;
        }

        return validComparisons > 0 ? totalSimilarity / validComparisons : 0.0;
    }


    private static double CalculateMapDominanceSimilarity(Dictionary<string, double> targetDominance, Dictionary<string, double> candidateDominance)
    {
        var commonMaps = targetDominance.Keys.Intersect(candidateDominance.Keys).ToList();

        if (!commonMaps.Any())
            return 0.0; // No common maps

        double totalSimilarity = 0;
        int validComparisons = 0;

        foreach (var map in commonMaps)
        {
            var targetScore = targetDominance[map];
            var candidateScore = candidateDominance[map];

            // Calculate similarity based on performance ratio difference
            var scoreDiff = Math.Abs(targetScore - candidateScore);

            // High similarity if performance ratios are close
            var similarity = Math.Max(0, 1.0 - scoreDiff);

            // Weight by dominance level (more weight for maps where both players perform well)
            var avgDominance = (targetScore + candidateScore) / 2.0;
            var weight = Math.Min(2.0, Math.Max(0.5, avgDominance)); // Weight between 0.5-2.0

            totalSimilarity += similarity * weight;
            validComparisons++;
        }

        return validComparisons > 0 ? totalSimilarity / validComparisons : 0.0;
    }

    private async Task<List<MilestoneAchievement>> GetPlayerMilestoneAchievements(string playerName)
    {
        await EnsureConnectionOpenAsync();

        // Get milestone achievements (excluding kill streaks as they are temporal)
        var query = $@"
SELECT 
    achievement_id,
    achievement_name,
    tier,
    value,
    achieved_at
FROM player_achievements_deduplicated
WHERE player_name = {Quote(playerName)} 
  AND achievement_type IN ('milestone', 'badge')
  AND achievement_id NOT LIKE 'kill_streak_%'
ORDER BY achieved_at DESC";

        var achievements = new List<MilestoneAchievement>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            achievements.Add(new MilestoneAchievement
            {
                AchievementId = reader.GetString(0),
                AchievementName = reader.GetString(1),
                Tier = reader.GetString(2),
                Value = reader.IsDBNull(3) ? 0 : Convert.ToUInt32(reader.GetValue(3)),
                AchievedAt = reader.GetDateTime(4)
            });
        }

        return achievements;
    }

    private static string Quote(string s) => $"'{s.Replace("'", "''")}'";

    /// <summary>
    /// Converts HeadToHeadSession data by replacing server GUIDs with server names
    /// </summary>
    private static List<HeadToHeadSession> ConvertHeadToHeadData(List<HeadToHeadSession> sessions, Dictionary<string, string> serverGuidToNameMapping)
    {
        foreach (var session in sessions)
        {
            session.ServerName = serverGuidToNameMapping.GetValueOrDefault(session.ServerName, session.ServerName);
        }
        return sessions;
    }

    /// <summary>
    /// Converts ServerDetails data by replacing server GUIDs with server names
    /// </summary>
    private static List<ServerDetails> ConvertCommonServersData(List<ServerDetails> servers, Dictionary<string, string> serverGuidToNameMapping)
    {
        foreach (var server in servers)
        {
            server.Name = serverGuidToNameMapping.GetValueOrDefault(server.Guid, server.Guid);
        }
        return servers;
    }

    /// <summary>
    /// Applies server GUID to name mapping to PlayerSimilarityStats
    /// </summary>
    private static void ApplyServerGuidToNameMapping(PlayerSimilarityStats playerStats, Dictionary<string, string> serverGuidToNameMapping)
    {
        // Convert favorite server GUID to name
        if (!string.IsNullOrEmpty(playerStats.FavoriteServerName))
        {
            playerStats.FavoriteServerName = serverGuidToNameMapping.GetValueOrDefault(playerStats.FavoriteServerName, playerStats.FavoriteServerName);
        }

        // Convert server ping GUIDs to names
        if (playerStats.ServerPings.Any())
        {
            var updatedServerPings = new Dictionary<string, double>();
            foreach (var kvp in playerStats.ServerPings)
            {
                var serverName = serverGuidToNameMapping.GetValueOrDefault(kvp.Key, kvp.Key);
                updatedServerPings[serverName] = kvp.Value;
            }
            playerStats.ServerPings = updatedServerPings;
        }
    }
}

// Result Models
public class PlayerComparisonResult
{
    public string Player1 { get; set; } = string.Empty;
    public string Player2 { get; set; } = string.Empty;
    public ServerDetails? ServerDetails { get; set; }
    public List<KillRateComparison> KillRates { get; set; } = new();
    public List<BucketTotalsComparison> BucketTotals { get; set; } = new();
    public List<PingComparison> AveragePing { get; set; } = new();
    public List<MapPerformanceComparison> MapPerformance { get; set; } = new();
    public List<HeadToHeadSession> HeadToHead { get; set; } = new();
    public List<ServerDetails> CommonServers { get; set; } = new();
    public List<KillMilestone> Player1KillMilestones { get; set; } = new();
    public List<KillMilestone> Player2KillMilestones { get; set; } = new();
    public List<MilestoneAchievement> Player1MilestoneAchievements { get; set; } = new();
    public List<MilestoneAchievement> Player2MilestoneAchievements { get; set; } = new();
}

public class KillRateComparison
{
    public string PlayerName { get; set; } = string.Empty;
    public double KillRate { get; set; }
}

public class BucketTotalsComparison
{
    public string Bucket { get; set; } = string.Empty;
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
    public string PlayerName { get; set; } = string.Empty;
    public double AveragePing { get; set; }
}

public class MapPerformanceComparison
{
    public string MapName { get; set; } = string.Empty;
    public PlayerTotals Player1Totals { get; set; } = new();
    public PlayerTotals Player2Totals { get; set; } = new();
}

public class HeadToHeadSession
{
    public DateTime Timestamp { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int Player1Score { get; set; }
    public int Player1Kills { get; set; }
    public int Player1Deaths { get; set; }
    public int Player2Score { get; set; }
    public int Player2Kills { get; set; }
    public int Player2Deaths { get; set; }
    public DateTime Player2Timestamp { get; set; }
    public string? RoundId { get; set; } // For UI linking to round details
}

public class ServerDetails
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public string GameId { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Timezone { get; set; }
    public string? Org { get; set; }
}

public class MilestoneAchievement
{
    public string AchievementId { get; set; } = string.Empty;
    public string AchievementName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public uint Value { get; set; }
    public DateTime AchievedAt { get; set; }
}

// Similar Players Feature Models
public class SimilarPlayersResult
{
    public string TargetPlayer { get; set; } = "";
    public PlayerSimilarityStats? TargetPlayerStats { get; set; }
    public List<SimilarPlayer> SimilarPlayers { get; set; } = new();
}

public class PlayerSimilarityStats
{
    public string PlayerName { get; set; } = "";
    public uint TotalKills { get; set; }
    public uint TotalDeaths { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double KillDeathRatio { get; set; }
    public double KillsPerMinute => TotalPlayTimeMinutes > 0 ? TotalKills / TotalPlayTimeMinutes : 0;
    public string FavoriteServerName { get; set; } = "";
    public double FavoriteServerPlayTimeMinutes { get; set; }
    public List<string> GameIds { get; set; } = new();
    public double TemporalOverlapMinutes { get; set; }
    public List<int> TypicalOnlineHours { get; set; } = new();
    public Dictionary<string, double> ServerPings { get; set; } = new(); // server_name -> average_ping
    public Dictionary<string, double> MapDominanceScores { get; set; } = new(); // map_name -> dominance_score
    public double TemporalNonOverlapScore { get; set; } // For alias detection: higher = less overlap
}

public class SimilarPlayer
{
    public string PlayerName { get; set; } = "";
    public uint TotalKills { get; set; }
    public uint TotalDeaths { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double KillDeathRatio { get; set; }
    public double KillsPerMinute { get; set; }
    public string FavoriteServerName { get; set; } = "";
    public double FavoriteServerPlayTimeMinutes { get; set; }
    public List<string> GameIds { get; set; } = new();
    public List<int> TypicalOnlineHours { get; set; } = new();
    public Dictionary<string, double> ServerPings { get; set; } = new(); // server_name -> average_ping
    public Dictionary<string, double> MapDominanceScores { get; set; } = new(); // map_name -> dominance_score
    public double TemporalNonOverlapScore { get; set; } // For alias detection: higher = less overlap
    public double TemporalOverlapMinutes { get; set; } // Actual overlap minutes with target player
    public double SimilarityScore { get; set; }
    public List<string> SimilarityReasons { get; set; } = new();
}

public class PlayerActivityHoursComparison
{
    public string Player1 { get; set; } = "";
    public string Player2 { get; set; } = "";
    public List<HourlyActivity> Player1ActivityHours { get; set; } = new();
    public List<HourlyActivity> Player2ActivityHours { get; set; } = new();
}

// Similarity algorithm modes
public enum SimilarityMode
{
    Default,        // General similarity based on play patterns and skills
    AliasDetection  // Focused on detecting same player using different aliases
}