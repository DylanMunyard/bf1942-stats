using api.PlayerTracking;
using api.ServerStats.Models;
using api.Caching;
using api.ClickHouse;
using api.ClickHouse.Interfaces;
using api.Gamification.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.ServerStats;

public class ServerStatsService(
    PlayerTrackerDbContext dbContext,
    ILogger<ServerStatsService> logger,
    ICacheService cacheService,
    ICacheKeyService cacheKeyService,
    PlayerRoundsReadService playerRoundsService,
    IClickHouseReader clickHouseReader,
    RoundsService roundsService,
    GameTrendsService gameTrendsService,
    PlayersOnlineHistoryService playersOnlineHistoryService) : IServerStatsService
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly ILogger<ServerStatsService> _logger = logger;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ICacheKeyService _cacheKeyService = cacheKeyService;
    private readonly PlayerRoundsReadService _playerRoundsService = playerRoundsService;
    private readonly IClickHouseReader _clickHouseReader = clickHouseReader;
    private readonly RoundsService _roundsService = roundsService;
    private readonly GameTrendsService _gameTrendsService = gameTrendsService;
    private readonly PlayersOnlineHistoryService _playersOnlineHistoryService = playersOnlineHistoryService;

    public async Task<ServerStatistics> GetServerStatistics(
        string serverName,
        int daysToAnalyze = 7)
    {
        // Check cache first - simplified cache key since we removed all leaderboard queries
        var cacheKey = _cacheKeyService.GetServerStatisticsKey(serverName, daysToAnalyze);
        var cachedResult = await _cacheService.GetAsync<ServerStatistics>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name - CurrentMap is now stored directly on the server
        var server = await _dbContext.Servers
            .Where(s => s.Name == serverName)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            _logger.LogWarning("Server not found: '{ServerName}'", serverName);
            return new ServerStatistics { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };
        }

        // Create the statistics object with only basic server metadata
        var statistics = new ServerStatistics
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            GameId = server.GameId,
            Region = server.Region ?? string.Empty,
            Country = server.Country ?? string.Empty,
            Timezone = server.Timezone ?? string.Empty,
            ServerIp = server.Ip,
            ServerPort = server.Port,
            DiscordUrl = server.DiscordUrl,
            ForumUrl = server.ForumUrl,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod,
            CurrentMap = server.CurrentMap
        };

        // Execute only recent rounds and busy indicator queries (no leaderboards)
        var recentRoundsTask = _roundsService.GetRecentRoundsAsync(server.Guid, 8);

        // Get busy indicator data for this server
        try
        {
            var busyIndicatorData = await GetServerBusyIndicatorAsync(server.Guid);
            statistics.BusyIndicator = busyIndicatorData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get busy indicator for server {ServerName} ({ServerGuid})", serverName, server.Guid);
            // Continue without busy indicator data rather than failing the entire request
        }

        statistics.RecentRounds = await recentRoundsTask;

        // Cache the result for 10 minutes
        await _cacheService.SetAsync(cacheKey, statistics, TimeSpan.FromMinutes(10));
        _logger.LogDebug("Cached server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

        return statistics;
    }

    /// <summary>
    /// Get server leaderboards for a specific time period
    /// </summary>
    /// <param name="serverName">Server name</param>
    /// <param name="days">Number of days to include in the leaderboards (e.g., 7, 30, 365)</param>
    /// <param name="minPlayersForWeighting">Optional minimum players for weighted placement leaderboards</param>
    /// <returns>Server leaderboards for the specified time period</returns>
    public async Task<ServerLeaderboards> GetServerLeaderboards(
        string serverName,
        int days = 7,
        int? minPlayersForWeighting = null)
    {
        // Validate days parameter
        if (days <= 0)
        {
            throw new ArgumentException("Days must be greater than 0", nameof(days));
        }

        // Check cache first
        var cacheKey = $"{_cacheKeyService.GetServerLeaderboardsKey(serverName, days)}_weight_{minPlayersForWeighting}";
        var cachedResult = await _cacheService.GetAsync<ServerLeaderboards>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server leaderboards: {ServerName}, {Days} days", serverName, days);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server leaderboards: {ServerName}, {Days} days", serverName, days);

        // Get the server by name
        var server = await _dbContext.Servers
            .Where(s => s.Name == serverName)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            _logger.LogWarning("Server not found: '{ServerName}'", serverName);
            return new ServerLeaderboards
            {
                ServerName = serverName,
                Days = days
            };
        }

        // Calculate time range based on days
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-days);

        _logger.LogInformation("Fetching leaderboards for {ServerName} ({ServerGuid}) from {StartPeriod} to {EndPeriod}",
            server.Name, server.Guid, startPeriod, endPeriod);

        // Create the leaderboards object
        var leaderboards = new ServerLeaderboards
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            Days = days,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Execute leaderboard queries in parallel for the specified time period
        try
        {
            var mostActivePlayersTask = _playerRoundsService.GetMostActivePlayersAsync(server.Guid, startPeriod, endPeriod, 10);
            var topScoresTask = _playerRoundsService.GetTopScoresAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKDRatiosTask = _playerRoundsService.GetTopKDRatiosAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKillRatesTask = _playerRoundsService.GetTopKillRatesAsync(server.Guid, startPeriod, endPeriod, 10);
            var topPlacementsTask = GetPlacementLeaderboardAsync(server.Guid, startPeriod, endPeriod, 10);

            // Wait for all queries to complete
            await Task.WhenAll(
                mostActivePlayersTask, topScoresTask, topKDRatiosTask, topKillRatesTask, topPlacementsTask
            );

            // Assign results
            leaderboards.MostActivePlayersByTime = await mostActivePlayersTask;
            leaderboards.TopScores = await topScoresTask;
            leaderboards.TopKDRatios = await topKDRatiosTask;
            leaderboards.TopKillRates = await topKillRatesTask;
            leaderboards.TopPlacements = await topPlacementsTask;

            _logger.LogInformation("Leaderboards fetched: MostActive={MostActiveCount}, TopScores={TopScoresCount}, TopKD={TopKDCount}, TopKillRate={TopKillRateCount}, TopPlacements={TopPlacementsCount}",
                leaderboards.MostActivePlayersByTime.Count,
                leaderboards.TopScores.Count,
                leaderboards.TopKDRatios.Count,
                leaderboards.TopKillRates.Count,
                leaderboards.TopPlacements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching leaderboards for server {ServerName} ({ServerGuid})", server.Name, server.Guid);
            // Return partial results - whatever was initialized will be empty lists
        }

        // If weighted placement is requested, fetch that as well
        if (minPlayersForWeighting.HasValue)
        {
            try
            {
                var minPlayers = minPlayersForWeighting.Value;
                leaderboards.MinPlayersForWeighting = minPlayers;

                var weightedPlacementsTask = GetWeightedPlacementLeaderboardAsync(server.Guid, startPeriod, endPeriod, 10, minPlayers);
                leaderboards.WeightedTopPlacements = await weightedPlacementsTask;

                _logger.LogInformation("Weighted placements fetched: Count={Count}", leaderboards.WeightedTopPlacements?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching weighted placements for server {ServerName} ({ServerGuid})", server.Name, server.Guid);
            }
        }

        // Cache the result for 10 minutes
        await _cacheService.SetAsync(cacheKey, leaderboards, TimeSpan.FromMinutes(10));
        _logger.LogDebug("Cached server leaderboards: {ServerName}, {Days} days", serverName, days);

        return leaderboards;
    }

    /// <summary>
    /// Get placement leaderboard for a specific server and time period.
    /// Returns players ranked by their placement achievements (gold, silver, bronze).
    /// </summary>
    private async Task<List<PlacementLeaderboardEntry>> GetPlacementLeaderboardAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10)
    {
        try
        {
            // Query placement achievements from ClickHouse
            // Group by player and count first/second/third place finishes
            // Order by Olympic-style ranking (gold first, then silver, then bronze)
            var query = $@"
SELECT 
    player_name,
    countIf(tier = 'gold') as first_places,
    countIf(tier = 'silver') as second_places,
    countIf(tier = 'bronze') as third_places
FROM player_achievements_deduplicated
WHERE achievement_type = 'round_placement'
    AND server_guid = '{serverGuid.Replace("'", "''")}'
    AND achieved_at >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
    AND achieved_at < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY player_name
HAVING first_places > 0 OR second_places > 0 OR third_places > 0
ORDER BY first_places DESC, second_places DESC, third_places DESC
LIMIT {limit}
FORMAT TabSeparated";

            _logger.LogDebug("Executing placement leaderboard query for server {ServerGuid} from {Start} to {End}",
                serverGuid, startPeriod, endPeriod);

            var result = await _clickHouseReader.ExecuteQueryAsync(query);
            var entries = new List<PlacementLeaderboardEntry>();

            var lines = result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length >= 4 &&
                    int.TryParse(parts[1], out var firstPlaces) &&
                    int.TryParse(parts[2], out var secondPlaces) &&
                    int.TryParse(parts[3], out var thirdPlaces))
                {
                    entries.Add(new PlacementLeaderboardEntry
                    {
                        Rank = i + 1,
                        PlayerName = parts[0],
                        FirstPlaces = firstPlaces,
                        SecondPlaces = secondPlaces,
                        ThirdPlaces = thirdPlaces
                    });
                }
            }

            _logger.LogDebug("Found {Count} placement leaderboard entries for server {ServerGuid}",
                entries.Count, serverGuid);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching placement leaderboard for server {ServerGuid}", serverGuid);
            return new List<PlacementLeaderboardEntry>();
        }
    }

    /// <summary>
    /// Get placement leaderboard for a specific server and time period.
    /// Returns players ranked by their placement achievements with simple point scoring (3,2,1 points).
    /// </summary>
    public async Task<List<PlacementLeaderboardEntry>> GetWeightedPlacementLeaderboardAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10,
        int minPlayerCount = 1)
    {
        try
        {
            // Query placement achievements from ClickHouse with JSON metadata parsing
            // Extract TotalPlayers from metadata and only count placements meeting minimum player count
            var query = $@"
SELECT 
    player_name,
    countIf(tier = 'gold' AND JSONExtract(metadata, 'TotalPlayers', 'Nullable(UInt32)') >= {minPlayerCount}) as first_places,
    countIf(tier = 'silver' AND JSONExtract(metadata, 'TotalPlayers', 'Nullable(UInt32)') >= {minPlayerCount}) as second_places,
    countIf(tier = 'bronze' AND JSONExtract(metadata, 'TotalPlayers', 'Nullable(UInt32)') >= {minPlayerCount}) as third_places
FROM player_achievements_deduplicated
WHERE achievement_type = 'round_placement'
    AND server_guid = '{serverGuid.Replace("'", "''")}'
    AND achieved_at >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
    AND achieved_at < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY player_name
HAVING first_places > 0 OR second_places > 0 OR third_places > 0
ORDER BY first_places DESC, second_places DESC, third_places DESC
LIMIT {limit}
FORMAT TabSeparated";

            _logger.LogDebug("Executing weighted placement leaderboard query for server {ServerGuid} from {Start} to {End} with minPlayerCount {MinPlayerCount}",
                serverGuid, startPeriod, endPeriod, minPlayerCount);

            var result = await _clickHouseReader.ExecuteQueryAsync(query);
            var entries = new List<PlacementLeaderboardEntry>();

            var lines = result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length >= 4 &&
                    int.TryParse(parts[1], out var firstPlaces) &&
                    int.TryParse(parts[2], out var secondPlaces) &&
                    int.TryParse(parts[3], out var thirdPlaces))
                {
                    entries.Add(new PlacementLeaderboardEntry
                    {
                        Rank = i + 1,
                        PlayerName = parts[0],
                        FirstPlaces = firstPlaces,
                        SecondPlaces = secondPlaces,
                        ThirdPlaces = thirdPlaces
                    });
                }
            }

            _logger.LogDebug("Found {Count} placement leaderboard entries for server {ServerGuid}",
                entries.Count, serverGuid);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching placement leaderboard for server {ServerGuid}", serverGuid);
            return new List<PlacementLeaderboardEntry>();
        }
    }

    public async Task<PagedResult<ServerRanking>> GetServerRankings(string serverName, int? year = null, int page = 1, int pageSize = 100,
        string? playerName = null, int? minScore = null, int? minKills = null, int? minDeaths = null,
        double? minKdRatio = null, int? minPlayTimeMinutes = null, string? orderBy = "TotalScore", string? orderDirection = "desc")
    {
        // Check cache first
        var cacheKey = _cacheKeyService.GetServerRankingsKey(serverName, year, page, pageSize, playerName, minScore, minKills, minDeaths, minKdRatio, minPlayTimeMinutes, orderBy, orderDirection);
        var cachedResult = await _cacheService.GetAsync<PagedResult<ServerRanking>>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server rankings: {ServerName}", serverName);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server rankings: {ServerName}", serverName);

        if (page < 1)
            throw new ArgumentException("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Page size must be between 1 and 100");

        // Validate orderBy parameter
        var validOrderByColumns = new[] { "TotalScore", "TotalKills", "TotalDeaths", "KDRatio", "TotalPlayTimeMinutes" };
        if (!string.IsNullOrEmpty(orderBy) && !validOrderByColumns.Contains(orderBy, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid orderBy column. Valid columns are: {string.Join(", ", validOrderByColumns)}");

        // Validate orderDirection parameter
        var validDirections = new[] { "asc", "desc" };
        if (!string.IsNullOrEmpty(orderDirection) && !validDirections.Contains(orderDirection, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Order direction must be 'asc' or 'desc'");

        // Set defaults and normalize case
        orderBy = orderBy ?? "TotalScore";
        orderDirection = orderDirection ?? "desc";
        var isDescending = orderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<ServerPlayerRanking> baseQuery = _dbContext.ServerPlayerRankings
            .Include(sr => sr.Player)
            .Where(sr => sr.Server.Name == serverName && !sr.Player.AiBot);

        // If year is provided, filter by year and month (use all months for the year)
        if (year.HasValue)
        {
            baseQuery = baseQuery.Where(sr => sr.Year == year.Value);
        }

        // Apply player name filter (case insensitive)
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            baseQuery = baseQuery.Where(sr => sr.PlayerName.ToLower().Contains(playerName.ToLower()));
        }

        // Get the aggregated data first, then apply numeric filters
        var playerStatsQuery = baseQuery
            .GroupBy(sr => sr.PlayerName)
            .Select(g => new
            {
                PlayerName = g.Key,
                TotalScore = g.Sum(r => r.TotalScore),
                TotalKills = g.Sum(r => r.TotalKills),
                TotalDeaths = g.Sum(r => r.TotalDeaths),
                TotalPlayTimeMinutes = g.Sum(r => r.TotalPlayTimeMinutes)
            });

        // Apply numeric filters
        if (minScore.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalScore >= minScore.Value);
        }

        if (minKills.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalKills >= minKills.Value);
        }

        if (minDeaths.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalDeaths >= minDeaths.Value);
        }

        if (minKdRatio.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x =>
                x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths >= minKdRatio.Value : x.TotalKills >= minKdRatio.Value);
        }

        if (minPlayTimeMinutes.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalPlayTimeMinutes >= minPlayTimeMinutes.Value);
        }

        // Get the total count of filtered players for pagination
        var totalItems = await playerStatsQuery.CountAsync();

        // Apply dynamic ordering and pagination
        var orderedQuery = orderBy.ToLowerInvariant() switch
        {
            "totalscore" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalScore) : playerStatsQuery.OrderBy(x => x.TotalScore),
            "totalkills" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalKills) : playerStatsQuery.OrderBy(x => x.TotalKills),
            "totaldeaths" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalDeaths) : playerStatsQuery.OrderBy(x => x.TotalDeaths),
            "totalplaytimeminutes" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalPlayTimeMinutes) : playerStatsQuery.OrderBy(x => x.TotalPlayTimeMinutes),
            "kdratio" => isDescending
                ? playerStatsQuery.OrderByDescending(x => x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths : x.TotalKills)
                : playerStatsQuery.OrderBy(x => x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths : x.TotalKills),
            _ => playerStatsQuery.OrderByDescending(x => x.TotalScore) // Default fallback
        };

        var finalQuery = orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        // Execute the query and materialize results
        var playerStats = await finalQuery.ToListAsync();

        // Now rank just the paged results in memory
        var items = playerStats
            .Select((x, index) => new ServerRanking
            {
                Rank = ((page - 1) * pageSize) + index + 1, // Calculate global rank based on page position
                ServerGuid = baseQuery.First().ServerGuid, // All records are for the same server
                ServerName = serverName, // Use the provided server name
                PlayerName = x.PlayerName,
                TotalScore = x.TotalScore,
                TotalKills = x.TotalKills,
                TotalDeaths = x.TotalDeaths,
                KDRatio = x.TotalDeaths > 0 ? Math.Round((double)x.TotalKills / x.TotalDeaths, 2) : x.TotalKills,
                TotalPlayTimeMinutes = x.TotalPlayTimeMinutes
            })
            .ToList();

        // Create minimal server context info
        var serverContext = new ServerContextInfo
        {
            ServerGuid = items.FirstOrDefault()?.ServerGuid,
            ServerName = serverName
        };

        var result = new PagedResult<ServerRanking>
        {
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
            Items = items,
            TotalItems = totalItems,
            ServerContext = serverContext
        };

        // Cache the result for 15 minutes
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));
        _logger.LogDebug("Cached server rankings: {ServerName}", serverName);

        return result;
    }


    // Removed legacy last rounds method that depended on HistoricalRoundsService






    public async Task<ServerInsights> GetServerInsights(string serverName, int days = 7)
    {
        // Validate days parameter
        if (days <= 0)
            throw new ArgumentException("Days must be greater than 0", nameof(days));

        // Calculate time periods and granularity based on days
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-days);
        var granularity = CalculateGranularity(days);

        // Check cache first
        var cacheKey = _cacheKeyService.GetServerInsightsKey(serverName, days);
        var cachedResult = await _cacheService.GetAsync<ServerInsights>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server insights: {ServerName}, days: {Days}", serverName, days);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server insights: {ServerName}, days: {Days}", serverName, days);

        // Get the server by name
        var server = await _dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new ServerInsights { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Create the insights object
        var insights = new ServerInsights
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Convert days to appropriate period string and rolling window
        var (period, rollingWindow) = ConvertDaysToPeriod(days);

        // Fetch players online history
        try
        {
            insights.PlayersOnlineHistory = await _playersOnlineHistoryService.GetPlayersOnlineHistory(
                server.GameId, period, rollingWindow, server.Guid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching players online history");
            insights.PlayersOnlineHistory = null;
        }

        // Cache the result for 20 minutes
        await _cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(20));

        return insights;
    }

    public async Task<ServerMapsInsights> GetServerMapsInsights(string serverName, int days = 7)
    {
        // Validate days parameter
        if (days <= 0)
            throw new ArgumentException("Days must be greater than 0", nameof(days));

        // Calculate time periods
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-days);

        // Check cache first
        var cacheKey = _cacheKeyService.GetServerMapsInsightsKey(serverName, days);
        var cachedResult = await _cacheService.GetAsync<ServerMapsInsights>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server maps insights: {ServerName}, days: {Days}", serverName, days);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server maps insights: {ServerName}, days: {Days}", serverName, days);

        // Get the server by name
        var server = await _dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new ServerMapsInsights { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Create the maps insights object
        var mapsInsights = new ServerMapsInsights
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Fetch maps data
        try
        {
            mapsInsights.Maps = await GetAllMapsFromClickHouse(server.Guid, startPeriod, endPeriod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching maps data");
            mapsInsights.Maps = [];
        }

        // Cache the result for 20 minutes
        await _cacheService.SetAsync(cacheKey, mapsInsights, TimeSpan.FromMinutes(20));

        return mapsInsights;
    }

    private TimeGranularity CalculateGranularity(int days)
    {
        return days switch
        {
            <= 7 => TimeGranularity.Hourly,
            <= 30 => TimeGranularity.FourHourly,
            <= 90 => TimeGranularity.Daily,
            <= 180 => TimeGranularity.Daily,
            _ => TimeGranularity.Weekly
        };
    }

    private static (string Period, int RollingWindow) ConvertDaysToPeriod(int days)
    {
        return days switch
        {
            1 => ("1d", 3),
            <= 3 => ("3d", 3),
            <= 7 => ("7d", 7),
            <= 30 => ("30d", 7),
            <= 90 => ("90d", 14),
            <= 180 => ("180d", 30),
            <= 365 => ("365d", 30),
            >= 36500 => ("alltime", 30),  // 100+ years = all time
            _ => ($"{days}d", 30)  // Support arbitrary day values
        };
    }

    private async Task<List<PopularMapDataPoint>> GetAllMapsFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod)
    {
        // Simple and reliable approach: aggregate by map directly
        var query = $@"
WITH map_data AS (
    SELECT 
        map_name,
        COUNT() AS data_points,
        AVG(players_online) AS avg_players,
        MAX(players_online) AS peak_players,
        MIN(timestamp) AS first_seen,
        MAX(timestamp) AS last_seen
    FROM server_online_counts
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND timestamp < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
        AND map_name != ''
    GROUP BY map_name
),
victory_data AS (
    SELECT 
        map_name,
        COUNT(DISTINCT CASE 
            WHEN JSONExtractInt(metadata, 'WinningTeam') = 1 THEN round_id 
            ELSE NULL 
        END) AS team1_victories,
        COUNT(DISTINCT CASE 
            WHEN JSONExtractInt(metadata, 'WinningTeam') = 2 THEN round_id 
            ELSE NULL 
        END) AS team2_victories,
        anyIf(JSONExtractString(metadata, 'WinningTeamLabel'), JSONExtractInt(metadata, 'WinningTeam') = 1) AS team1_label,
        anyIf(JSONExtractString(metadata, 'WinningTeamLabel'), JSONExtractInt(metadata, 'WinningTeam') = 2) AS team2_label
    FROM player_achievements_deduplicated
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND achievement_type = 'team_victory'
        AND achieved_at >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND achieved_at < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
        AND map_name != ''
    GROUP BY map_name
),
total_data AS (
    SELECT SUM(data_points) AS total_points
    FROM map_data
)
SELECT 
    md.map_name,
    ROUND(md.avg_players, 2) AS avg_players,
    md.peak_players,
    ROUND(md.data_points * 0.5, 0) AS estimated_play_time_minutes, -- 30 seconds per data point = 0.5 minutes
    ROUND(md.data_points * 100.0 / NULLIF(td.total_points, 0), 2) AS play_time_percentage,
    COALESCE(vd.team1_victories, 0) AS team1_victories,
    COALESCE(vd.team2_victories, 0) AS team2_victories,
    vd.team1_label,
    vd.team2_label
FROM map_data md
LEFT JOIN victory_data vd ON md.map_name = vd.map_name
CROSS JOIN total_data td
ORDER BY md.data_points DESC, md.avg_players DESC
FORMAT TabSeparated";

        _logger.LogDebug("=== ALL MAPS QUERY (30-second intervals) ===");
        _logger.LogDebug("Server GUID: {ServerGuid}", serverGuid);
        _logger.LogDebug("Start Period: {StartPeriod}", startPeriod);
        _logger.LogDebug("End Period: {EndPeriod}", endPeriod);
        _logger.LogDebug("FULL QUERY:\n{Query}", query);

        var result = await _clickHouseReader.ExecuteQueryAsync(query);

        _logger.LogDebug("RAW RESULT:\n{Result}", result);

        var allMaps = new List<PopularMapDataPoint>();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
        {
            var parts = line.Split('\t');
            if (parts.Length >= 9)
            {
                if (double.TryParse(parts[1], out var avgPlayers) &&
                    int.TryParse(parts[2], out var peakPlayers) &&
                    int.TryParse(parts[3], out var totalPlayTime) &&
                    double.TryParse(parts[4], out var playTimePercentage) &&
                    int.TryParse(parts[5], out var team1Victories) &&
                    int.TryParse(parts[6], out var team2Victories))
                {
                    allMaps.Add(new PopularMapDataPoint
                    {
                        MapName = parts[0],
                        AveragePlayerCount = Math.Round(avgPlayers, 2),
                        PeakPlayerCount = peakPlayers,
                        TotalPlayTime = totalPlayTime,
                        PlayTimePercentage = Math.Round(playTimePercentage, 2),
                        Team1Victories = team1Victories,
                        Team2Victories = team2Victories,
                        Team1Label = string.IsNullOrEmpty(parts[7]) ? null : parts[7],
                        Team2Label = string.IsNullOrEmpty(parts[8]) ? null : parts[8]
                    });
                }
            }
        }

        _logger.LogDebug("All maps count: {Count}", allMaps.Count);

        return allMaps;
    }


    private string GetClickHouseTimeGrouping(TimeGranularity granularity)
    {
        return granularity switch
        {
            TimeGranularity.Hourly => "toStartOfHour(round_start_time)",
            TimeGranularity.FourHourly => "toDateTime(toUnixTimestamp(toStartOfHour(round_start_time)) - (toUnixTimestamp(toStartOfHour(round_start_time)) % 14400))",
            TimeGranularity.Daily => "toStartOfDay(round_start_time)",
            TimeGranularity.Weekly => "toMonday(round_start_time)",
            TimeGranularity.Monthly => "toStartOfMonth(round_start_time)",
            _ => throw new ArgumentException($"Invalid granularity: {granularity}")
        };
    }

    public async Task<PagedResult<ServerBasicInfo>> GetAllServersWithPaging(
        int page = 1,
        int pageSize = 50,
        string sortBy = "ServerName",
        string sortOrder = "asc",
        ServerFilters? filters = null)
    {
        // Check cache first
        var cacheKey = _cacheKeyService.GetServersPageKey(page, pageSize, sortBy, sortOrder, filters);
        var cachedResult = await _cacheService.GetAsync<PagedResult<ServerBasicInfo>>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server search: page {Page}, pageSize {PageSize}", page, pageSize);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server search: page {Page}, pageSize {PageSize}", page, pageSize);

        if (page < 1)
            throw new ArgumentException("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            throw new ArgumentException("Page size must be between 1 and 500");

        // Validate sortBy parameter
        var validSortFields = new[] { "ServerName", "GameId", "Country", "Region", "TotalPlayersAllTime", "TotalActivePlayersLast24h", "LastActivity" };
        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        // Validate sortOrder parameter
        var validDirections = new[] { "asc", "desc" };
        if (!validDirections.Contains(sortOrder, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Sort order must be 'asc' or 'desc'");

        filters ??= new ServerFilters();

        // Base query for servers
        IQueryable<GameServer> baseQuery = _dbContext.Servers.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.ServerName))
        {
            baseQuery = baseQuery.Where(s => s.Name.ToLower().Contains(filters.ServerName.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(filters.GameId))
        {
            baseQuery = baseQuery.Where(s => s.GameId == filters.GameId);
        }

        if (!string.IsNullOrWhiteSpace(filters.Game))
        {
            baseQuery = baseQuery.Where(s => s.Game == filters.Game);
        }

        if (!string.IsNullOrWhiteSpace(filters.Country))
        {
            baseQuery = baseQuery.Where(s => s.Country != null && s.Country.ToLower().Contains(filters.Country.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(filters.Region))
        {
            baseQuery = baseQuery.Where(s => s.Region != null && s.Region.ToLower().Contains(filters.Region.ToLower()));
        }

        // Calculate last activity and player counts
        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);

        var serversWithStats = baseQuery.Select(s => new
        {
            Server = s,
            LastActivity = s.Sessions
                .Where(session => !session.IsActive)
                .Max(session => (DateTime?)session.LastSeenTime) ??
                s.Sessions
                .Where(session => session.IsActive)
                .Max(session => (DateTime?)session.StartTime),
            HasActivePlayers = s.Sessions.Any(session => session.IsActive),
            CurrentMap = s.CurrentMap,
            TotalPlayersAllTime = s.Sessions.Select(session => session.PlayerName).Distinct().Count(),
            TotalActivePlayersLast24h = s.Sessions
                .Where(session => session.LastSeenTime >= last24Hours)
                .Select(session => session.PlayerName)
                .Distinct()
                .Count()
        });

        // Apply additional filters based on calculated fields
        if (filters.HasActivePlayers.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.HasActivePlayers == filters.HasActivePlayers.Value);
        }

        if (filters.LastActivityFrom.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.LastActivity >= filters.LastActivityFrom.Value);
        }

        if (filters.LastActivityTo.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.LastActivity <= filters.LastActivityTo.Value);
        }

        if (filters.MinTotalPlayers.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.TotalPlayersAllTime >= filters.MinTotalPlayers.Value);
        }

        if (filters.MaxTotalPlayers.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.TotalPlayersAllTime <= filters.MaxTotalPlayers.Value);
        }

        if (filters.MinActivePlayersLast24h.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.TotalActivePlayersLast24h >= filters.MinActivePlayersLast24h.Value);
        }

        if (filters.MaxActivePlayersLast24h.HasValue)
        {
            serversWithStats = serversWithStats.Where(s => s.TotalActivePlayersLast24h <= filters.MaxActivePlayersLast24h.Value);
        }

        // Get total count for pagination
        var totalItems = await serversWithStats.CountAsync();

        // Apply sorting
        var isDescending = sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var orderedQuery = sortBy.ToLowerInvariant() switch
        {
            "servername" => isDescending ? serversWithStats.OrderByDescending(s => s.Server.Name) : serversWithStats.OrderBy(s => s.Server.Name),
            "gameid" => isDescending ? serversWithStats.OrderByDescending(s => s.Server.GameId) : serversWithStats.OrderBy(s => s.Server.GameId),
            "country" => isDescending ? serversWithStats.OrderByDescending(s => s.Server.Country) : serversWithStats.OrderBy(s => s.Server.Country),
            "region" => isDescending ? serversWithStats.OrderByDescending(s => s.Server.Region) : serversWithStats.OrderBy(s => s.Server.Region),
            "totalplayersalltime" => isDescending ? serversWithStats.OrderByDescending(s => s.TotalPlayersAllTime) : serversWithStats.OrderBy(s => s.TotalPlayersAllTime),
            "totalactiveplayerslast24h" => isDescending ? serversWithStats.OrderByDescending(s => s.TotalActivePlayersLast24h) : serversWithStats.OrderBy(s => s.TotalActivePlayersLast24h),
            "lastactivity" => isDescending ? serversWithStats.OrderByDescending(s => s.LastActivity) : serversWithStats.OrderBy(s => s.LastActivity),
            _ => serversWithStats.OrderBy(s => s.Server.Name) // Default fallback
        };

        // Apply pagination
        var pagedQuery = orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        // Execute query and materialize results
        var serverData = await pagedQuery.ToListAsync();

        // Map to ServerBasicInfo
        var items = serverData.Select(s => new ServerBasicInfo
        {
            ServerGuid = s.Server.Guid,
            ServerName = s.Server.Name,
            GameId = s.Server.GameId,
            ServerIp = s.Server.Ip,
            ServerPort = s.Server.Port,
            Country = s.Server.Country,
            Region = s.Server.Region,
            City = s.Server.City,
            Timezone = s.Server.Timezone,
            TotalActivePlayersLast24h = s.TotalActivePlayersLast24h,
            TotalPlayersAllTime = s.TotalPlayersAllTime,
            CurrentMap = s.CurrentMap,
            HasActivePlayers = s.HasActivePlayers,
            LastActivity = s.LastActivity,
            DiscordUrl = s.Server.DiscordUrl,
            ForumUrl = s.Server.ForumUrl
        }).ToList();

        var result = new PagedResult<ServerBasicInfo>
        {
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
            Items = items,
            TotalItems = totalItems
        };

        // Cache the result for 5 minutes (servers change less frequently than players)
        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        _logger.LogDebug("Cached server search results: page {Page}, pageSize {PageSize}", page, pageSize);

        return result;
    }

    /// <summary>
    /// Get busy indicator data for a single server with 8 hours before/after forecast timeline
    /// </summary>
    private async Task<ServerBusyIndicator> GetServerBusyIndicatorAsync(string serverGuid)
    {
        var currentTime = DateTime.UtcNow;
        var currentHour = currentTime.Hour;
        var currentDayOfWeek = (int)currentTime.DayOfWeek;
        var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

        // Get busy indicator data from GameTrendsService for this single server with 8 hours before/after
        var busyIndicatorResult = await _gameTrendsService.GetServerBusyIndicatorAsync(new[] { serverGuid }, timelineHourRange: 8);
        var serverResult = busyIndicatorResult.ServerResults.FirstOrDefault();

        if (serverResult == null)
        {
            // Return empty/unknown data if no results
            return new ServerBusyIndicator
            {
                BusyIndicator = new BusyIndicatorData
                {
                    BusyLevel = "unknown",
                    BusyText = "Not enough data",
                    CurrentPlayers = 0,
                    TypicalPlayers = 0,
                    Percentile = 0,
                    GeneratedAt = DateTime.UtcNow
                },
                HourlyTimeline = new List<api.ServerStats.Models.HourlyBusyData>(),
                GeneratedAt = DateTime.UtcNow
            };
        }

        // Convert GameTrendsService models to ServerStats models
        var busyIndicator = new ServerBusyIndicator
        {
            BusyIndicator = new BusyIndicatorData
            {
                BusyLevel = serverResult.BusyIndicator.BusyLevel,
                BusyText = serverResult.BusyIndicator.BusyText,
                CurrentPlayers = serverResult.BusyIndicator.CurrentPlayers,
                TypicalPlayers = serverResult.BusyIndicator.TypicalPlayers,
                Percentile = serverResult.BusyIndicator.Percentile,
                HistoricalRange = serverResult.BusyIndicator.HistoricalRange != null ?
                    new api.ServerStats.Models.HistoricalRange
                    {
                        Min = serverResult.BusyIndicator.HistoricalRange.Min,
                        Q25 = serverResult.BusyIndicator.HistoricalRange.Q25,
                        Median = serverResult.BusyIndicator.HistoricalRange.Median,
                        Q75 = serverResult.BusyIndicator.HistoricalRange.Q75,
                        Q90 = serverResult.BusyIndicator.HistoricalRange.Q90,
                        Max = serverResult.BusyIndicator.HistoricalRange.Max,
                        Average = serverResult.BusyIndicator.HistoricalRange.Average
                    } : null,
                GeneratedAt = serverResult.BusyIndicator.GeneratedAt
            },
            HourlyTimeline = serverResult.HourlyTimeline?.Select(ht => new api.ServerStats.Models.HourlyBusyData
            {
                Hour = ht.Hour,
                TypicalPlayers = ht.TypicalPlayers,
                BusyLevel = ht.BusyLevel,
                IsCurrentHour = ht.IsCurrentHour
            }).ToList() ?? new List<api.ServerStats.Models.HourlyBusyData>(),
            GeneratedAt = busyIndicatorResult.GeneratedAt
        };

        return busyIndicator;
    }

}
