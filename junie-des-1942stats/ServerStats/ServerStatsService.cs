using System.Data.SqlTypes;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using junie_des_1942stats.Caching;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.Gamification.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ServerStats;

// Helper class for raw SQL query results
public class PingTimestampData
{
    public DateTime Timestamp { get; set; }
    public int Ping { get; set; }
}

public class ServerStatsService(
    PlayerTrackerDbContext dbContext,
    ILogger<ServerStatsService> logger,
    ICacheService cacheService,
    ICacheKeyService cacheKeyService,
    PlayerRoundsReadService playerRoundsService,
    IClickHouseReader clickHouseReader,
    RoundsService roundsService)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly ILogger<ServerStatsService> _logger = logger;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ICacheKeyService _cacheKeyService = cacheKeyService;
    private readonly PlayerRoundsReadService _playerRoundsService = playerRoundsService;
    private readonly IClickHouseReader _clickHouseReader = clickHouseReader;
    private readonly RoundsService _roundsService = roundsService;

    public async Task<ServerStatistics> GetServerStatistics(
        string serverName,
        int daysToAnalyze = 7)
    {
        // Check cache first
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

        // Get the server by name and current map in one query
        var serverWithCurrentMap = await _dbContext.Servers
            .Where(s => s.Name == serverName)
            .Select(s => new
            {
                Server = s,
                CurrentMap = s.Sessions
                    .Where(session => session.IsActive)
                    .Select(session => session.MapName)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        var server = serverWithCurrentMap?.Server;

        if (server == null)
        {
            _logger.LogWarning("Server not found: '{ServerName}'", serverName);
            return new ServerStatistics { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };
        }

        // Create the statistics object
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
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Define time periods
        var oneWeekStart = endPeriod.AddDays(-7);
        var oneMonthStart = endPeriod.AddDays(-30);
        var allTimeStart = new DateTime(2000, 1, 1); // Start from year 2000

        // Execute all leaderboard queries in parallel for maximum performance
        var mostActivePlayersWeekTask = _playerRoundsService.GetMostActivePlayersAsync(server.Guid, oneWeekStart, endPeriod, 10);
        var topScoresWeekTask = _playerRoundsService.GetTopScoresAsync(server.Guid, oneWeekStart, endPeriod, 10);
        var topKDRatiosWeekTask = _playerRoundsService.GetTopKDRatiosAsync(server.Guid, oneWeekStart, endPeriod, 10);
        var topKillRatesWeekTask = _playerRoundsService.GetTopKillRatesAsync(server.Guid, oneWeekStart, endPeriod, 10);

        var mostActivePlayersMonthTask = _playerRoundsService.GetMostActivePlayersAsync(server.Guid, oneMonthStart, endPeriod, 10);
        var topScoresMonthTask = _playerRoundsService.GetTopScoresAsync(server.Guid, oneMonthStart, endPeriod, 10);
        var topKDRatiosMonthTask = _playerRoundsService.GetTopKDRatiosAsync(server.Guid, oneMonthStart, endPeriod, 10);
        var topKillRatesMonthTask = _playerRoundsService.GetTopKillRatesAsync(server.Guid, oneMonthStart, endPeriod, 10);

        var mostActivePlayersAllTimeTask = _playerRoundsService.GetMostActivePlayersAsync(server.Guid, allTimeStart, endPeriod, 10);
        var topScoresAllTimeTask = _playerRoundsService.GetTopScoresAsync(server.Guid, allTimeStart, endPeriod, 10);
        var topKDRatiosAllTimeTask = _playerRoundsService.GetTopKDRatiosAsync(server.Guid, allTimeStart, endPeriod, 10);
        var topKillRatesAllTimeTask = _playerRoundsService.GetTopKillRatesAsync(server.Guid, allTimeStart, endPeriod, 10);

        // Execute placement queries in parallel
        var topPlacementsWeekTask = GetPlacementLeaderboardAsync(server.Guid, oneWeekStart, endPeriod, 10);
        var topPlacementsMonthTask = GetPlacementLeaderboardAsync(server.Guid, oneMonthStart, endPeriod, 10);
        var topPlacementsAllTimeTask = GetPlacementLeaderboardAsync(server.Guid, allTimeStart, endPeriod, 10);

        // Execute recent rounds query independently
        var recentRoundsTask = _roundsService.GetRecentRoundsAsync(server.Guid, 20);

        // Wait for all queries to complete
        await Task.WhenAll(
            mostActivePlayersWeekTask, topScoresWeekTask, topKDRatiosWeekTask, topKillRatesWeekTask,
            mostActivePlayersMonthTask, topScoresMonthTask, topKDRatiosMonthTask, topKillRatesMonthTask,
            mostActivePlayersAllTimeTask, topScoresAllTimeTask, topKDRatiosAllTimeTask, topKillRatesAllTimeTask,
            topPlacementsWeekTask, topPlacementsMonthTask, topPlacementsAllTimeTask,
            recentRoundsTask
        );

        // Assign results to statistics object
        statistics.MostActivePlayersByTimeWeek = await mostActivePlayersWeekTask;
        statistics.TopScoresWeek = await topScoresWeekTask;
        statistics.TopKDRatiosWeek = await topKDRatiosWeekTask;
        statistics.TopKillRatesWeek = await topKillRatesWeekTask;

        statistics.MostActivePlayersByTimeMonth = await mostActivePlayersMonthTask;
        statistics.TopScoresMonth = await topScoresMonthTask;
        statistics.TopKDRatiosMonth = await topKDRatiosMonthTask;
        statistics.TopKillRatesMonth = await topKillRatesMonthTask;

        statistics.MostActivePlayersByTimeAllTime = await mostActivePlayersAllTimeTask;
        statistics.TopScoresAllTime = await topScoresAllTimeTask;
        statistics.TopKDRatiosAllTime = await topKDRatiosAllTimeTask;
        statistics.TopKillRatesAllTime = await topKillRatesAllTimeTask;

        statistics.TopPlacementsWeek = await topPlacementsWeekTask;
        statistics.TopPlacementsMonth = await topPlacementsMonthTask;
        statistics.TopPlacementsAllTime = await topPlacementsAllTimeTask;

        statistics.RecentRounds = await recentRoundsTask;

        // Set current map from the combined query
        statistics.CurrentMap = serverWithCurrentMap?.CurrentMap;

        // Cache the result for 10 minutes
        await _cacheService.SetAsync(cacheKey, statistics, TimeSpan.FromMinutes(10));
        _logger.LogDebug("Cached server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

        return statistics;
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
FROM player_achievements
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


    public async Task<SessionRoundReport?> GetRoundReport(string serverGuid, string mapName, DateTime startTime)
    {
        // Find a representative session for this round
        var representativeSession = await _dbContext.PlayerSessions
            .Include(s => s.Server)
            .Where(s => s.ServerGuid == serverGuid &&
                       s.MapName == mapName &&
                       s.StartTime <= startTime &&
                       s.LastSeenTime >= startTime)
            .FirstOrDefaultAsync();

        if (representativeSession == null)
            return null;

        return await GetRoundReportInternal(serverGuid, mapName, startTime, representativeSession);
    }

    private async Task<SessionRoundReport?> GetRoundReportInternal(string serverGuid, string mapName, DateTime referenceTime, PlayerSession representativeSession)
    {
        // Find the previous session on the same server with a different map (to determine the actual round start)
        var previousMapSession = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == serverGuid &&
                        s.MapName != mapName &&
                        s.StartTime < referenceTime)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        var actualRoundStart = previousMapSession != null
            ? previousMapSession.LastSeenTime // Round starts when the previous map's session ended
            : referenceTime.AddMinutes(-30); // Fallback to 30min buffer

        // Find the next session on the same server with a different map (to determine the actual round end)
        var nextMapSession = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == serverGuid &&
                        s.MapName != mapName &&
                        s.StartTime > referenceTime)
            .OrderBy(s => s.StartTime)
            .FirstOrDefaultAsync();

        var actualRoundEnd = nextMapSession != null
            ? nextMapSession.StartTime // Round ends when the next map's session starts
            : referenceTime.AddMinutes(30); // Fallback to 30min buffer

        // Get all sessions in the round (same server and map, within the calculated round boundaries)
        var roundSessions = await _dbContext.PlayerSessions
            .Include(s => s.Server)
            .Where(s => s.ServerGuid == serverGuid &&
                       s.MapName == mapName &&
                       s.StartTime <= actualRoundEnd &&
                       s.LastSeenTime >= actualRoundStart)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (!roundSessions.Any())
            return null;

        // Get all observations for the round with player names
        var roundObservations = await _dbContext.PlayerObservations
            .Include(o => o.Session)
            .Where(o => roundSessions.Select(s => s.SessionId).Contains(o.SessionId))
            .OrderBy(o => o.Timestamp)
            .Select(o => new
            {
                o.Timestamp,
                o.Score,
                o.Kills,
                o.Deaths,
                o.Ping,
                o.Team,
                o.TeamLabel,
                PlayerName = o.Session.PlayerName
            })
            .ToListAsync();

        // Create leaderboard snapshots starting from actual round start
        var leaderboardSnapshots = new List<ServerStats.Models.LeaderboardSnapshot>();
        var currentTime = actualRoundStart; // Start from earliest session time

        while (currentTime <= actualRoundEnd)
        {
            // Get the latest score for each player at this time
            var playerScores = roundObservations
                .Where(o => o.Timestamp <= currentTime)
                .GroupBy(o => o.PlayerName)
                .Select(g =>
                {
                    var obs = g.OrderByDescending(x => x.Timestamp).First();
                    return new
                    {
                        PlayerName = g.Key,
                        Score = obs.Score,
                        Kills = obs.Kills,
                        Deaths = obs.Deaths,
                        Ping = obs.Ping,
                        Team = obs.Team,
                        TeamLabel = obs.TeamLabel,
                        LastSeen = obs.Timestamp
                    };
                })
                .Where(x => x.LastSeen >= currentTime.AddMinutes(-1)) // Only include players seen in last minute
                .OrderByDescending(x => x.Score)
                .Select((x, i) => new ServerStats.Models.LeaderboardEntry
                {
                    Rank = i + 1,
                    PlayerName = x.PlayerName,
                    Score = x.Score,
                    Kills = x.Kills,
                    Deaths = x.Deaths,
                    Ping = x.Ping,
                    Team = x.Team,
                    TeamLabel = x.TeamLabel
                })
                .ToList();

            leaderboardSnapshots.Add(new ServerStats.Models.LeaderboardSnapshot
            {
                Timestamp = currentTime,
                Entries = playerScores
            });

            currentTime = currentTime.AddMinutes(1);
        }

        // Filter out empty snapshots
        leaderboardSnapshots = leaderboardSnapshots
            .Where(snapshot => snapshot.Entries.Any())
            .ToList();

        return new SessionRoundReport
        {
            Session = new SessionInfo
            {
                SessionId = representativeSession.SessionId,
                RoundId = representativeSession.RoundId,
                PlayerName = representativeSession.PlayerName,
                ServerName = representativeSession.Server.Name,
                ServerGuid = representativeSession.ServerGuid,
                GameId = representativeSession.Server.GameId,
                Kills = representativeSession.TotalKills,
                Deaths = representativeSession.TotalDeaths,
                Score = representativeSession.TotalScore,
                ServerIp = representativeSession.Server.Ip,
                ServerPort = representativeSession.Server.Port
            },
            Round = new RoundReportInfo
            {
                MapName = mapName,
                GameType = representativeSession.GameType ?? "",
                StartTime = actualRoundStart,
                EndTime = actualRoundEnd,
                TotalParticipants = roundSessions.Count,
                IsActive = roundSessions.Any(s => s.IsActive)
            },
            LeaderboardSnapshots = leaderboardSnapshots
        };
    }

    public async Task<ServerInsights> GetServerInsights(string serverName, string period = "7d")
    {
        // Validate and parse period
        var (startPeriod, endPeriod, granularity) = ParsePeriod(period);

        // Check cache first
        var cacheKey = _cacheKeyService.GetServerInsightsKey(serverName, period);
        var cachedResult = await _cacheService.GetAsync<ServerInsights>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server insights: {ServerName}, period: {Period}", serverName, period);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server insights: {ServerName}, period: {Period}", serverName, period);

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

        // Get comprehensive player count data from ClickHouse with comparison period
        try
        {
            var playerCountData = await GetPlayerCountDataWithComparisonFromClickHouse(server.Guid, startPeriod, endPeriod, granularity);
            
            insights.PlayerCountHistory = playerCountData.History ?? new List<PlayerCountDataPoint>();
            insights.PlayerCountHistoryComparison = playerCountData.HistoryComparison ?? new List<PlayerCountDataPoint>();
            insights.PlayerCountSummary = playerCountData.Summary;
        }
        catch (Exception ex)
        {
            // Log the error but continue with empty metrics
            _logger.LogError(ex, "Error fetching player count data from ClickHouse");
            insights.PlayerCountHistory = [];
            insights.PlayerCountHistoryComparison = [];
            insights.PlayerCountSummary = null;
        }

        // Get all maps data
        try
        {
            var allMaps = await GetAllMapsFromClickHouse(server.Guid, startPeriod, endPeriod);
            insights.Maps = allMaps;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching maps data from ClickHouse");
            insights.Maps = [];
        }

        // Use ClickHouse to calculate ping statistics with appropriate granularity
        try
        {
            var timeGrouping = GetTimeGroupingFunction(granularity);
            var query = $@"
WITH filtered_pings AS (
    SELECT 
        {timeGrouping} as time_period,
        ping
    FROM player_metrics
    WHERE server_guid = '{server.Guid.Replace("'", "''")}'
        AND timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND timestamp <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
        AND ping > 0 
        AND ping < 1000  -- Filter out unrealistic ping values
)
SELECT 
    time_period,
    round(avg(ping), 2) as avg_ping,
    round(quantile(0.5)(ping), 2) as median_ping,
    round(quantile(0.95)(ping), 2) as p95_ping
FROM filtered_pings
GROUP BY time_period
ORDER BY time_period
FORMAT TabSeparated";

            var result = await _clickHouseReader.ExecuteQueryAsync(query);
            var pingData = new List<PingDataPoint>();

            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 4)
                {
                    var timePeriod = ParseTimePeriod(parts[0], granularity);
                    pingData.Add(new PingDataPoint
                    {
                        TimePeriod = timePeriod,
                        AveragePing = double.Parse(parts[1]),
                        MedianPing = double.Parse(parts[2]),
                        P95Ping = Math.Round(double.Parse(parts[3]), 2)
                    });
                }
            }

            insights.PingByHour = new PingByHourInsight
            {
                Data = pingData
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ping data from ClickHouse");
            insights.PingByHour = new PingByHourInsight { Data = [] };
        }

        // Cache the result for 20 minutes
        await _cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(20));

        return insights;
    }

    private (DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity) ParsePeriod(string period)
    {
        var endPeriod = DateTime.UtcNow;
        DateTime startPeriod;
        TimeGranularity granularity;

        switch (period.ToLowerInvariant())
        {
            case "7d":
                startPeriod = endPeriod.AddDays(-7);
                granularity = TimeGranularity.Hourly;
                break;
            case "1m":
                startPeriod = endPeriod.AddDays(-30);
                granularity = TimeGranularity.FourHourly;
                break;
            case "3m":
                startPeriod = endPeriod.AddDays(-90);
                granularity = TimeGranularity.Daily;
                break;
            case "6m":
                startPeriod = endPeriod.AddDays(-180);
                granularity = TimeGranularity.Daily;
                break;
            case "1y":
                startPeriod = endPeriod.AddDays(-365);
                granularity = TimeGranularity.Weekly;
                break;
            default:
                throw new ArgumentException($"Invalid period '{period}'. Valid periods are: 7d, 1m, 3m, 6m, 1y");
        }

        return (startPeriod, endPeriod, granularity);
    }


    private string GetTimeGroupingFunction(TimeGranularity granularity)
    {
        return granularity switch
        {
            TimeGranularity.Hourly => "toStartOfHour(timestamp)",
            TimeGranularity.FourHourly => "toDateTime(toUnixTimestamp(toStartOfHour(timestamp)) - (toUnixTimestamp(toStartOfHour(timestamp)) % 14400))",
            TimeGranularity.Daily => "toStartOfDay(timestamp)",
            TimeGranularity.Weekly => "toMonday(timestamp)",
            TimeGranularity.Monthly => "toStartOfMonth(timestamp)",
            _ => throw new ArgumentException($"Invalid granularity: {granularity}")
        };
    }

    private DateTime ParseTimePeriod(string timePeriodStr, TimeGranularity granularity)
    {
        if (!DateTime.TryParse(timePeriodStr, out var parsed))
        {
            _logger.LogWarning("Failed to parse time period '{TimePeriodStr}' for granularity {Granularity}. Using DateTime.MinValue.", timePeriodStr, granularity);
            return DateTime.MinValue;
        }

        return parsed;
    }

    private async Task<(List<PlayerCountDataPoint> History, PlayerCountSummary Summary)> GetPlayerCountDataFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        // Use separate simpler queries for reliability
        var historyTask = GetPlayerCountHistoryFromClickHouse(serverGuid, startPeriod, endPeriod, granularity);
        var summaryTask = GetPlayerCountSummaryFromClickHouse(serverGuid, startPeriod, endPeriod, granularity);

        await Task.WhenAll(historyTask, summaryTask);

        var history = await historyTask;
        var summary = await summaryTask;

        return (history, summary);
    }

    private async Task<(List<PlayerCountDataPoint> History, List<PlayerCountDataPoint> HistoryComparison, PlayerCountSummary Summary)> 
        GetPlayerCountDataWithComparisonFromClickHouse(string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        // Use separate queries for reliability
        var historyWithComparisonTask = GetPlayerCountHistoryWithComparisonFromClickHouse(serverGuid, startPeriod, endPeriod, granularity);
        var summaryTask = GetPlayerCountSummaryFromClickHouse(serverGuid, startPeriod, endPeriod, granularity);

        await Task.WhenAll(historyWithComparisonTask, summaryTask);

        var (currentHistory, comparisonHistory) = await historyWithComparisonTask;
        var summary = await summaryTask;

        return (currentHistory, comparisonHistory, summary);
    }

    private async Task<List<PlayerCountDataPoint>> GetPlayerCountHistoryFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        // Simple time grouping for server_online_counts timestamp column
        var timeGrouping = granularity switch
        {
            TimeGranularity.Hourly => "toStartOfHour(timestamp)",
            TimeGranularity.FourHourly => "toDateTime(toUnixTimestamp(toStartOfHour(timestamp)) - (toUnixTimestamp(toStartOfHour(timestamp)) % 14400))",
            TimeGranularity.Daily => "toStartOfDay(timestamp)", 
            TimeGranularity.Weekly => "toMonday(timestamp)",
            TimeGranularity.Monthly => "toStartOfMonth(timestamp)",
            _ => "toStartOfHour(timestamp)"
        };

        // Use Grafana-style sampling: take the last (most recent) value in each time bucket
        // This preserves actual player counts without smoothing
        var query = $@"
SELECT 
    {timeGrouping} as time_bucket,
    argMax(players_online, timestamp) as players_online
FROM server_online_counts
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
    AND timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
    AND timestamp < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY time_bucket
ORDER BY time_bucket
FORMAT TabSeparated";

        var result = await _clickHouseReader.ExecuteQueryAsync(query);

        var history = new List<PlayerCountDataPoint>();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
        {
            var parts = line.Split('\t');

            if (parts.Length >= 2 &&
                DateTime.TryParse(parts[0], out var timestamp) &&
                int.TryParse(parts[1], out var playersOnline))
            {
                history.Add(new PlayerCountDataPoint
                {
                    Timestamp = timestamp,
                    PlayerCount = playersOnline, // Actual player count, not averaged!
                    UniquePlayersStarted = playersOnline // For compatibility
                });
            }
        }

        return history;
    }

    private async Task<(List<PlayerCountDataPoint> Current, List<PlayerCountDataPoint> Comparison)> 
        GetPlayerCountHistoryWithComparisonFromClickHouse(
            string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        // Calculate comparison period (same duration, but shifted back)
        var totalDays = (int)(endPeriod - startPeriod).TotalDays;
        var comparisonEndPeriod = startPeriod;
        var comparisonStartPeriod = startPeriod.AddDays(-totalDays);

        // Simple time grouping for server_online_counts timestamp column
        var timeGrouping = granularity switch
        {
            TimeGranularity.Hourly => "toStartOfHour(timestamp)",
            TimeGranularity.FourHourly => "toDateTime(toUnixTimestamp(toStartOfHour(timestamp)) - (toUnixTimestamp(toStartOfHour(timestamp)) % 14400))",
            TimeGranularity.Daily => "toStartOfDay(timestamp)", 
            TimeGranularity.Weekly => "toMonday(timestamp)",
            TimeGranularity.Monthly => "toStartOfMonth(timestamp)",
            _ => "toStartOfHour(timestamp)"
        };

        // Query both current and comparison periods in a single query for consistency
        var query = $@"
WITH current_period AS (
    SELECT 
        {timeGrouping} as time_bucket,
        argMax(players_online, timestamp) as players_online,
        'current' as period_type
    FROM server_online_counts
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND timestamp < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
    GROUP BY time_bucket
),
comparison_period AS (
    SELECT 
        {timeGrouping} as time_bucket,
        argMax(players_online, timestamp) as players_online,
        'comparison' as period_type
    FROM server_online_counts
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND timestamp >= '{comparisonStartPeriod:yyyy-MM-dd HH:mm:ss}'
        AND timestamp < '{comparisonEndPeriod:yyyy-MM-dd HH:mm:ss}'
    GROUP BY time_bucket
)
SELECT 
    time_bucket,
    players_online,
    period_type
FROM current_period
UNION ALL
SELECT 
    time_bucket,
    players_online,
    period_type
FROM comparison_period
ORDER BY period_type, time_bucket
FORMAT TabSeparated";

        var result = await _clickHouseReader.ExecuteQueryAsync(query);

        var currentHistory = new List<PlayerCountDataPoint>();
        var comparisonHistory = new List<PlayerCountDataPoint>();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 &&
                DateTime.TryParse(parts[0], out var timestamp) &&
                int.TryParse(parts[1], out var playersOnline) &&
                !string.IsNullOrEmpty(parts[2]))
            {
                var dataPoint = new PlayerCountDataPoint
                {
                    Timestamp = timestamp,
                    PlayerCount = playersOnline,
                    UniquePlayersStarted = playersOnline // For compatibility
                };

                if (parts[2] == "current")
                {
                    currentHistory.Add(dataPoint);
                }
                else if (parts[2] == "comparison")
                {
                    // Adjust comparison timestamps to align with current period for UI display
                    var adjustedTimestamp = timestamp.AddDays(totalDays);
                    dataPoint.Timestamp = adjustedTimestamp;
                    comparisonHistory.Add(dataPoint);
                }
            }
        }

        return (currentHistory, comparisonHistory);
    }

    private async Task<PlayerCountSummary> GetPlayerCountSummaryFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        var totalDays = (int)(endPeriod - startPeriod).TotalDays;
        var halfPeriodDays = totalDays / 2;
        var midPeriod = startPeriod.AddDays(halfPeriodDays);

        // Use the same time grouping as history for consistency
        var timeGrouping = granularity switch
        {
            TimeGranularity.Hourly => "toStartOfHour(timestamp)",
            TimeGranularity.FourHourly => "toDateTime(toUnixTimestamp(toStartOfHour(timestamp)) - (toUnixTimestamp(toStartOfHour(timestamp)) % 14400))",
            TimeGranularity.Daily => "toStartOfDay(timestamp)", 
            TimeGranularity.Weekly => "toMonday(timestamp)",
            TimeGranularity.Monthly => "toStartOfMonth(timestamp)",
            _ => "toStartOfHour(timestamp)"
        };

        // Use server_online_counts with the SAME sampling as history to ensure consistency
        var query = $@"
WITH sampled_data AS (
    SELECT 
        {timeGrouping} as time_bucket,
        argMax(players_online, timestamp) as players_online,
        argMax(timestamp, timestamp) as bucket_timestamp
    FROM server_online_counts
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND timestamp < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
    GROUP BY time_bucket
),
summary AS (
    SELECT 
        AVG(players_online) as avg_players,
        MAX(players_online) as peak_players,
        argMax(bucket_timestamp, players_online) as peak_timestamp
    FROM sampled_data
),
period_comparison AS (
    SELECT 
        CASE 
            WHEN bucket_timestamp >= '{startPeriod:yyyy-MM-dd HH:mm:ss}' 
                AND bucket_timestamp < '{midPeriod:yyyy-MM-dd HH:mm:ss}' THEN 'older'
            WHEN bucket_timestamp >= '{midPeriod:yyyy-MM-dd HH:mm:ss}' 
                AND bucket_timestamp < '{endPeriod:yyyy-MM-dd HH:mm:ss}' THEN 'recent'
        END as period_type,
        AVG(players_online) as avg_players
    FROM sampled_data
    WHERE bucket_timestamp IS NOT NULL
    GROUP BY period_type
),
unique_players AS (
    SELECT COUNT(DISTINCT player_name) as total_unique
    FROM player_rounds
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
        AND is_bot = 0
)
SELECT 
    s.avg_players,
    s.peak_players,
    s.peak_timestamp,
    up.total_unique,
    COALESCE(
        ROUND((recent.avg_players - older.avg_players) / NULLIF(older.avg_players, 0) * 100),
        0
    ) as change_percent
FROM summary s
CROSS JOIN unique_players up
LEFT JOIN (SELECT avg_players FROM period_comparison WHERE period_type = 'recent') recent ON 1=1
LEFT JOIN (SELECT avg_players FROM period_comparison WHERE period_type = 'older') older ON 1=1
FORMAT TabSeparated";

        _logger.LogDebug("=== PLAYER COUNT SUMMARY QUERY (Consistent Sampling) ===");
        _logger.LogDebug("Server GUID: {ServerGuid}", serverGuid);
        _logger.LogDebug("Start Period: {StartPeriod}", startPeriod);
        _logger.LogDebug("End Period: {EndPeriod}", endPeriod);
        _logger.LogDebug("Mid Period: {MidPeriod}", midPeriod);
        _logger.LogDebug("Granularity: {Granularity}", granularity);
        _logger.LogDebug("Time Grouping: {TimeGrouping}", timeGrouping);
        _logger.LogDebug("FULL QUERY:\n{Query}", query);

        var result = await _clickHouseReader.ExecuteQueryAsync(query);

        _logger.LogDebug("RAW RESULT:\n{Result}", result);
        _logger.LogDebug("Result length: {Length} characters", result?.Length ?? 0);
        _logger.LogDebug("Result lines: {Lines}", result?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length ?? 0);

        var summary = new PlayerCountSummary();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
        {
            var parts = line.Split('\t');
            if (parts.Length >= 5)
            {
                if (double.TryParse(parts[0], out var avgPlayers) &&
                    int.TryParse(parts[1], out var peakPlayers) &&
                    DateTime.TryParse(parts[2], out var peakTime) &&
                    int.TryParse(parts[3], out var totalUnique) &&
                    int.TryParse(parts[4], out var changePercent))
                {
                    summary.AveragePlayerCount = Math.Round(avgPlayers, 2);
                    summary.PeakPlayerCount = peakPlayers;
                    summary.PeakTimestamp = peakTime;
                    summary.TotalUniquePlayersInPeriod = totalUnique;
                    summary.ChangePercentFromPreviousPeriod = changePercent == 0 ? null : changePercent;
                }
            }
        }

        return summary;
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
total_data AS (
    SELECT SUM(data_points) AS total_points
    FROM map_data
)
SELECT 
    md.map_name,
    ROUND(md.avg_players, 2) AS avg_players,
    md.peak_players,
    ROUND(md.data_points * 0.5, 0) AS estimated_play_time_minutes, -- 30 seconds per data point = 0.5 minutes
    ROUND(md.data_points * 100.0 / NULLIF(td.total_points, 0), 2) AS play_time_percentage
FROM map_data md
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
            if (parts.Length >= 5)
            {
                if (double.TryParse(parts[1], out var avgPlayers) &&
                    int.TryParse(parts[2], out var peakPlayers) &&
                    int.TryParse(parts[3], out var totalPlayTime) &&
                    double.TryParse(parts[4], out var playTimePercentage))
                {
                    allMaps.Add(new PopularMapDataPoint
                    {
                        MapName = parts[0],
                        AveragePlayerCount = Math.Round(avgPlayers, 2),
                        PeakPlayerCount = peakPlayers,
                        TotalPlayTime = totalPlayTime,
                        PlayTimePercentage = Math.Round(playTimePercentage, 2)
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
            CurrentMap = s.Sessions
                .Where(session => session.IsActive)
                .Select(session => session.MapName)
                .FirstOrDefault(),
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
            LastActivity = s.LastActivity
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

}

public enum TimeGranularity
{
    Hourly,
    FourHourly,
    Daily,
    Weekly,
    Monthly
}