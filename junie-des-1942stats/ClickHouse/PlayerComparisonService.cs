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

public class PlayerComparisonService
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

    private async Task<List<HeadToHeadSession>> GetHeadToHead(string player1, string player2, string? serverGuid = null)
    {
        // Find overlapping rounds using the player_rounds table
        var serverFilter = !string.IsNullOrEmpty(serverGuid) ? $" AND p1.server_guid = {Quote(serverGuid)}" : "";
        var query = $@"
SELECT p1.round_start_time, p1.round_end_time, p1.server_guid, p1.map_name,
       p1.final_score, p1.final_kills, p1.final_deaths,
       p2.final_score, p2.final_kills, p2.final_deaths,
       p2.round_start_time, p2.round_end_time
FROM player_rounds p1
JOIN player_rounds p2 ON p1.server_guid = p2.server_guid 
    AND p1.map_name = p2.map_name
    AND p1.round_start_time <= p2.round_end_time 
    AND p2.round_start_time <= p1.round_end_time
WHERE p1.player_name = {Quote(player1)} AND p2.player_name = {Quote(player2)}{serverFilter}
ORDER BY p1.round_start_time DESC
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
                Player2Deaths = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9)),
                Player2Timestamp = reader.IsDBNull(10) ? DateTime.MinValue : reader.GetDateTime(10)
            });
        }
        return sessions;
    }

    private async Task<List<ServerDetails>> GetCommonServers(string player1, string player2)
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

    public async Task<PlayerActivityHoursComparison> ComparePlayersActivityHoursAsync(string player1, string player2)
    {
        await EnsureConnectionOpenAsync();

        var result = new PlayerActivityHoursComparison
        {
            Player1 = player1,
            Player2 = player2
        };

        // Get activity hours for both players using the same logic as single player insights
        result.Player1ActivityHours = await GetPlayerActivityHours(player1);
        result.Player2ActivityHours = await GetPlayerActivityHours(player2);

        return result;
    }

    private async Task<List<HourlyActivity>> GetPlayerActivityHours(string playerName)
    {
        // Use the same approach as PlayerStatsService but with ClickHouse player_rounds data
        var query = $@"
SELECT 
    toHour(round_start_time) as hour_of_day,
    SUM(play_time_minutes) as total_minutes
FROM player_rounds
WHERE player_name = {Quote(playerName)} 
  AND round_start_time >= now() - INTERVAL 6 MONTH
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

        // First, get the target player's stats to compare against
        var targetStats = await GetPlayerStatsForSimilarity(targetPlayer);
        if (targetStats == null)
        {
            _logger.LogWarning("Target player {PlayerName} not found", targetPlayer);
            return result;
        }

        result.TargetPlayerStats = targetStats;

        // Get target player's typical online hours if temporal filtering is enabled
        List<int>? targetOnlineHours = null;
        if (filterBySimilarOnlineTime)
        {
            targetOnlineHours = await GetPlayerTypicalOnlineHours(targetPlayer);
        }

        // Find similar players based on multiple criteria
        var similarPlayers = await FindPlayersBySimilarity(targetPlayer, targetStats, limit * 3, targetOnlineHours, mode); // Get more candidates

        // Calculate similarity scores and rank them
        var minThreshold = mode == SimilarityMode.AliasDetection ? 0.3 : 0.1; // Higher threshold for alias detection
        var rankedPlayers = similarPlayers
            .Select(p => CalculateSimilarityScore(targetStats, p, mode))
            .Where(p => p.SimilarityScore > minThreshold)
            .OrderByDescending(p => p.SimilarityScore)
            .Take(limit)
            .ToList();

        result.SimilarPlayers = rankedPlayers;

        return result;
    }

    private async Task<PlayerSimilarityStats?> GetPlayerStatsForSimilarity(string playerName)
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

            var playerStats = new PlayerSimilarityStats
            {
                PlayerName = playerName,
                TotalKills = totalKills,
                TotalDeaths = totalDeaths,
                TotalPlayTimeMinutes = totalPlayTime,
                KillDeathRatio = totalDeaths > 0 ? (double)totalKills / totalDeaths : totalKills,
                FavoriteServerGuid = favoriteServerGuid,
                FavoriteServerPlayTimeMinutes = favoriteServerMinutes,
                GameIds = gameIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            };

            // Get player's typical online hours
            playerStats.TypicalOnlineHours = await GetPlayerTypicalOnlineHours(playerName);
            
            // Get server-specific ping data for alias detection
            playerStats.ServerPings = await GetPlayerServerPings(playerName);
            
            // Get map dominance scores for alias detection
            playerStats.MapDominanceScores = await GetPlayerMapDominanceScores(playerName);
            
            return playerStats;
        }

        return null;
    }

    private async Task<List<PlayerSimilarityStats>> FindPlayersBySimilarity(string targetPlayer, PlayerSimilarityStats targetStats, int limit, List<int>? targetOnlineHours = null, SimilarityMode mode = SimilarityMode.Default)
    {
        // Adjust search criteria based on similarity mode
        var playTimeMin = mode == SimilarityMode.AliasDetection 
            ? targetStats.TotalPlayTimeMinutes * 0.5  // Wider play time range for aliases
            : targetStats.TotalPlayTimeMinutes * 0.7; // ±30% play time range for default
        var playTimeMax = mode == SimilarityMode.AliasDetection 
            ? targetStats.TotalPlayTimeMinutes * 1.5
            : targetStats.TotalPlayTimeMinutes * 1.3;
        
        var kdrTolerance = mode == SimilarityMode.AliasDetection ? 0.2 : 0.4; // Tighter KDR tolerance for aliases
        var kdrMin = Math.Max(0, targetStats.KillDeathRatio - kdrTolerance);
        var kdrMax = targetStats.KillDeathRatio + kdrTolerance;

        // Create game_id filter - only include players who have played the same games
        var gameIdFilter = "";
        if (targetStats.GameIds.Any())
        {
            var gameIdList = string.Join(", ", targetStats.GameIds.Select(Quote));
            gameIdFilter = $" AND game_id IN ({gameIdList})";
        }

        // Build temporal filter if target online hours are provided
        var temporalFilter = "";
        if (targetOnlineHours != null && targetOnlineHours.Any())
        {
            var hoursList = string.Join(",", targetOnlineHours);
            temporalFilter = $@"
temporal_overlap AS (
    SELECT 
        player_name,
        SUM(play_time_minutes) as overlap_minutes
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} 
      AND round_start_time >= now() - INTERVAL 6 MONTH{gameIdFilter}
      AND toHour(round_start_time) IN ({hoursList})
    GROUP BY player_name
    HAVING overlap_minutes >= 30  -- At least 30 minutes of overlap
),";
        }

        var query = $@"
WITH player_stats AS (
    SELECT 
        player_name,
        SUM(final_kills) AS total_kills,
        SUM(final_deaths) AS total_deaths,
        SUM(play_time_minutes) AS total_play_time_minutes,
        CASE WHEN SUM(final_deaths) > 0 THEN SUM(final_kills) / SUM(final_deaths) ELSE toFloat64(SUM(final_kills)) END AS kdr
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{gameIdFilter}
    GROUP BY player_name
    HAVING total_play_time_minutes BETWEEN {playTimeMin} AND {playTimeMax}
       AND kdr BETWEEN {kdrMin} AND {kdrMax}
),
{temporalFilter}
server_playtime AS (
    SELECT player_name, server_guid, SUM(play_time_minutes) AS total_minutes
    FROM player_rounds
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{gameIdFilter}
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
    WHERE player_name != {Quote(targetPlayer)} AND round_start_time >= now() - INTERVAL 6 MONTH{gameIdFilter}
    GROUP BY player_name
)
SELECT 
    p.player_name,
    p.total_kills,
    p.total_deaths,
    p.total_play_time_minutes,
    p.kdr,
    f.server_guid as favorite_server_guid,
    f.total_minutes as favorite_server_minutes,
    g.game_ids{(targetOnlineHours != null && targetOnlineHours.Any() ? ",\n    t.overlap_minutes" : "")}
FROM player_stats p
LEFT JOIN favorite_servers f ON p.player_name = f.player_name AND f.rn = 1
LEFT JOIN player_game_ids g ON p.player_name = g.player_name{(targetOnlineHours != null && targetOnlineHours.Any() ? "\nINNER JOIN temporal_overlap t ON p.player_name = t.player_name" : "")}
ORDER BY abs(p.total_play_time_minutes - {targetStats.TotalPlayTimeMinutes}) + 
         abs(p.kdr - {targetStats.KillDeathRatio}) * 100
LIMIT {limit}";

        var players = new List<PlayerSimilarityStats>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
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
            var temporalOverlapMinutes = 0.0;
            
            // Read temporal overlap if available
            if (targetOnlineHours != null && targetOnlineHours.Any())
            {
                temporalOverlapMinutes = reader.IsDBNull(8) ? 0.0 : Convert.ToDouble(reader.GetValue(8));
            }

            var playerStats = new PlayerSimilarityStats
            {
                PlayerName = playerName,
                TotalKills = totalKills,
                TotalDeaths = totalDeaths,
                TotalPlayTimeMinutes = totalPlayTime,
                KillDeathRatio = kdr,
                FavoriteServerGuid = favoriteServerGuid,
                FavoriteServerPlayTimeMinutes = favoriteServerMinutes,
                GameIds = gameIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                TemporalOverlapMinutes = temporalOverlapMinutes
            };

            // Get the candidate player's typical online hours if temporal filtering is enabled
            if (targetOnlineHours != null && targetOnlineHours.Any())
            {
                playerStats.TypicalOnlineHours = await GetPlayerTypicalOnlineHours(playerName);
            }

            // For alias detection mode, also get ping and dominance data
            if (mode == SimilarityMode.AliasDetection)
            {
                playerStats.ServerPings = await GetPlayerServerPings(playerName);
                playerStats.MapDominanceScores = await GetPlayerMapDominanceScores(playerName);
            }

            players.Add(playerStats);
        }

        return players;
    }

    private SimilarPlayer CalculateSimilarityScore(PlayerSimilarityStats target, PlayerSimilarityStats candidate, SimilarityMode mode = SimilarityMode.Default)
    {
        double score = 0;
        var reasons = new List<string>();
        
        // Determine if temporal similarity is available
        var hasTemporalData = target.TypicalOnlineHours.Any() && candidate.TypicalOnlineHours.Any();
        
        // Adjust weights based on similarity mode and temporal data availability
        double playTimeWeight, kdrWeight, serverWeight, temporalWeight, pingWeight, mapDominanceWeight;
        
        if (mode == SimilarityMode.AliasDetection)
        {
            // For alias detection, prioritize ping similarity and behavioral patterns
            playTimeWeight = hasTemporalData ? 0.1 : 0.15;
            kdrWeight = hasTemporalData ? 0.25 : 0.3;      // Important but not primary
            serverWeight = hasTemporalData ? 0.15 : 0.2;   // Server affinity important
            temporalWeight = hasTemporalData ? 0.2 : 0.0;  // Online patterns
            pingWeight = 0.25;                             // PRIMARY: ping similarity for aliases
            mapDominanceWeight = 0.15;                     // Map performance patterns
        }
        else
        {
            // Default algorithm weights (no ping/dominance analysis)
            playTimeWeight = hasTemporalData ? 0.2 : 0.3;
            kdrWeight = hasTemporalData ? 0.35 : 0.4;
            serverWeight = hasTemporalData ? 0.2 : 0.3;
            temporalWeight = hasTemporalData ? 0.25 : 0.0;
            pingWeight = 0.0;
            mapDominanceWeight = 0.0;
        }

        // Play time similarity
        var playTimeDiff = Math.Abs(target.TotalPlayTimeMinutes - candidate.TotalPlayTimeMinutes);
        var playTimeRatio = Math.Max(target.TotalPlayTimeMinutes, candidate.TotalPlayTimeMinutes);
        var playTimeScore = playTimeRatio > 0 ? Math.Max(0, 1 - (playTimeDiff / playTimeRatio)) : 1;
        score += playTimeScore * playTimeWeight;
        
        if (playTimeScore > 0.8)
            reasons.Add($"Similar play time ({candidate.TotalPlayTimeMinutes:F0} vs {target.TotalPlayTimeMinutes:F0} minutes)");

        // KDR similarity
        var kdrDiff = Math.Abs(target.KillDeathRatio - candidate.KillDeathRatio);
        var kdrScore = Math.Max(0, Math.Min(1, 1 - (kdrDiff / 2.0))); // Normalize KDR diff
        score += kdrScore * kdrWeight;
        
        if (kdrScore > 0.7)
            reasons.Add($"Similar KDR ({candidate.KillDeathRatio:F2} vs {target.KillDeathRatio:F2})");

        // Server affinity
        var serverScore = target.FavoriteServerGuid == candidate.FavoriteServerGuid ? 1.0 : 0.0;
        score += serverScore * serverWeight;
        
        if (serverScore > 0)
            reasons.Add("Plays on same favorite server");

        // Temporal similarity (overlap in online hours)
        if (hasTemporalData)
        {
            var commonHours = target.TypicalOnlineHours.Intersect(candidate.TypicalOnlineHours).Count();
            var totalUniqueHours = target.TypicalOnlineHours.Union(candidate.TypicalOnlineHours).Count();
            var temporalScore = totalUniqueHours > 0 ? (double)commonHours / totalUniqueHours : 0;
            score += temporalScore * temporalWeight;
            
            if (temporalScore > 0.5)
            {
                var overlapHours = target.TypicalOnlineHours.Intersect(candidate.TypicalOnlineHours).OrderBy(h => h).ToList();
                var hoursText = string.Join(", ", overlapHours.Select(h => $"{h:D2}:00"));
                reasons.Add($"Similar online times ({overlapHours.Count} overlapping hours: {hoursText})");
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

        return new SimilarPlayer
        {
            PlayerName = candidate.PlayerName,
            TotalKills = candidate.TotalKills,
            TotalDeaths = candidate.TotalDeaths,
            TotalPlayTimeMinutes = candidate.TotalPlayTimeMinutes,
            KillDeathRatio = candidate.KillDeathRatio,
            FavoriteServerGuid = candidate.FavoriteServerGuid,
            SimilarityScore = score,
            SimilarityReasons = reasons
        };
    }

    private async Task<List<int>> GetPlayerTypicalOnlineHours(string playerName)
    {
        var query = $@"
WITH hourly_playtime AS (
    SELECT 
        toHour(round_start_time) as hour_of_day,
        SUM(play_time_minutes) as total_minutes
    FROM player_rounds
    WHERE player_name = {Quote(playerName)} 
      AND round_start_time >= now() - INTERVAL 6 MONTH
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

    private async Task<Dictionary<string, double>> GetPlayerServerPings(string playerName)
    {
        var query = $@"
SELECT 
    server_guid,
    avg(ping) as avg_ping
FROM player_metrics
WHERE player_name = {Quote(playerName)} 
  AND ping > 0 
  AND ping < 1000  -- Filter out unrealistic ping values
  AND timestamp >= now() - INTERVAL 30 DAY  -- Recent ping data for accuracy
GROUP BY server_guid
HAVING count(*) >= 10  -- Require at least 10 measurements for reliability";

        var serverPings = new Dictionary<string, double>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = query;
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var serverGuid = reader.GetString(0);
            var avgPing = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1));
            serverPings[serverGuid] = avgPing;
        }

        return serverPings;
    }

    private async Task<Dictionary<string, double>> GetPlayerMapDominanceScores(string playerName)
    {
        // Calculate dominance as the ratio of player's performance vs average performance on each map
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
      AND play_time_minutes > 5  -- Exclude very short sessions
    GROUP BY map_name
    HAVING total_play_time >= 60  -- At least 1 hour on the map
),
map_averages AS (
    SELECT 
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as avg_score_rate
    FROM player_rounds
    WHERE round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5
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
            
            // Calculate similarity based on ping difference (2-3ms variance for aliases)
            var pingDiff = Math.Abs(targetPing - candidatePing);
            
            // High similarity if within 3ms, decreasing to 0 at 20ms difference
            var similarity = Math.Max(0, 1.0 - (pingDiff / 20.0));
            
            // Bonus for very close pings (within 3ms - likely same physical location)
            if (pingDiff <= 3.0)
                similarity = Math.Min(1.0, similarity + 0.2); // Boost for alias-like ping similarity
            
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

    private static string Quote(string s) => $"'{s.Replace("'", "''")}'";
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
    public string ServerGuid { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int Player1Score { get; set; }
    public int Player1Kills { get; set; }
    public int Player1Deaths { get; set; }
    public int Player2Score { get; set; }
    public int Player2Kills { get; set; }
    public int Player2Deaths { get; set; }
    public DateTime Player2Timestamp { get; set; }
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
    public string FavoriteServerGuid { get; set; } = "";
    public double FavoriteServerPlayTimeMinutes { get; set; }
    public List<string> GameIds { get; set; } = new();
    public double TemporalOverlapMinutes { get; set; }
    public List<int> TypicalOnlineHours { get; set; } = new();
    public Dictionary<string, double> ServerPings { get; set; } = new(); // server_guid -> average_ping
    public Dictionary<string, double> MapDominanceScores { get; set; } = new(); // map_name -> dominance_score
}

public class SimilarPlayer
{
    public string PlayerName { get; set; } = "";
    public uint TotalKills { get; set; }
    public uint TotalDeaths { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double KillDeathRatio { get; set; }
    public string FavoriteServerGuid { get; set; } = "";
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