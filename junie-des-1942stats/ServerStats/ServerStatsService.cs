using System.Data.SqlTypes;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using junie_des_1942stats.Caching;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
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
    HistoricalRoundsService historicalRoundsService)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly ILogger<ServerStatsService> _logger = logger;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ICacheKeyService _cacheKeyService = cacheKeyService;
    private readonly PlayerRoundsReadService _playerRoundsService = playerRoundsService;
    private readonly IClickHouseReader _clickHouseReader = clickHouseReader;
    private readonly HistoricalRoundsService _historicalRoundsService = historicalRoundsService;

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

        // Get most active players for 1 week (7 days)
        var oneWeekStart = endPeriod.AddDays(-7);
        var mostActivePlayersWeek = await _playerRoundsService.GetMostActivePlayersAsync(server.Guid, oneWeekStart, endPeriod, 10);

        statistics.MostActivePlayersByTimeWeek = mostActivePlayersWeek;

        // Get top scores for 1 week (7 days)
        var topScoresWeek = await _playerRoundsService.GetTopScoresAsync(server.Guid, oneWeekStart, endPeriod, 10);

        statistics.TopScoresWeek = topScoresWeek;

        // Get most active players for 1 month (30 days)
        var oneMonthStart = endPeriod.AddDays(-30);
        var mostActivePlayersMonth = await _playerRoundsService.GetMostActivePlayersAsync(server.Guid, oneMonthStart, endPeriod, 10);

        statistics.MostActivePlayersByTimeMonth = mostActivePlayersMonth;

        // Get top scores for 1 month (30 days)
        var topScoresMonth = await _playerRoundsService.GetTopScoresAsync(server.Guid, oneMonthStart, endPeriod, 10);

        statistics.TopScoresMonth = topScoresMonth;

        // Get the last 5 rounds (unique maps) showing when each map was last played
        // Use a fixed 5-hour window for recent map rotations (much faster than analyzing days of data)
        var recentRoundsStart = DateTime.UtcNow.AddHours(-5);
        var lastRounds = await GetLastRoundsAsync(server.Guid, 5);

        statistics.LastRounds = lastRounds;

        // Set current map from the combined query
        statistics.CurrentMap = serverWithCurrentMap?.CurrentMap;

        // Cache the result for 10 minutes
        await _cacheService.SetAsync(cacheKey, statistics, TimeSpan.FromMinutes(10));
        _logger.LogDebug("Cached server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

        return statistics;
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
            .Where(sr => sr.Server.Name == serverName);

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


    private async Task<List<RoundInfo>> GetLastRoundsAsync(string serverGuid, int limit)
    {
        var filters = new RoundFilters { ServerGuid = serverGuid };
        var roundsResult = await _historicalRoundsService.GetAllRounds(1, limit, "StartTime", "desc", filters);

        return roundsResult.Items.Select(r => new RoundInfo
        {
            MapName = r.MapName,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            IsActive = r.IsActive,
        }).ToList();
    }


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
        var leaderboardSnapshots = new List<LeaderboardSnapshot>();
        var currentTime = actualRoundStart; // Start from earliest session time
        
        while (currentTime <= actualRoundEnd)
        {
            // Get the latest score for each player at this time
            var playerScores = roundObservations
                .Where(o => o.Timestamp <= currentTime)
                .GroupBy(o => o.PlayerName)
                .Select(g => {
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
                .Select((x, i) => new LeaderboardEntry
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
                
            leaderboardSnapshots.Add(new LeaderboardSnapshot
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

        // Get comprehensive player count data from ClickHouse using single optimized query
        _logger.LogDebug("=== STARTING PLAYER COUNT DATA FETCH ===");
        _logger.LogDebug("Server GUID: {ServerGuid}", server.Guid);
        _logger.LogDebug("Server Name: {ServerName}", server.Name);
        _logger.LogDebug("Start Period: {StartPeriod}", startPeriod);
        _logger.LogDebug("End Period: {EndPeriod}", endPeriod);
        _logger.LogDebug("Granularity: {Granularity}", granularity);
        
        try
        {
            var playerCountData = await GetPlayerCountDataFromClickHouse(server.Guid, startPeriod, endPeriod, granularity);
            _logger.LogDebug("Player count data retrieved - History: {HistoryCount}, Summary: {Summary}", 
                playerCountData.History?.Count ?? 0, 
                playerCountData.Summary != null ? "Not null" : "NULL");
            
            insights.PlayerCountHistory = playerCountData.History ?? new List<PlayerCountDataPoint>();
            insights.PlayerCountSummary = playerCountData.Summary;
        }
        catch (Exception ex)
        {
            // Log the error but continue with empty metrics
            _logger.LogError(ex, "Error fetching player count data from ClickHouse");
            insights.PlayerCountHistory = [];
            insights.PlayerCountSummary = null;
        }
        
        _logger.LogDebug("=== FINAL INSIGHT RESULTS ===");
        _logger.LogDebug("PlayerCountHistory count: {Count}", insights.PlayerCountHistory?.Count ?? 0);
        _logger.LogDebug("PlayerCountSummary: {Summary}", insights.PlayerCountSummary != null ? "Present" : "NULL");

        // Use ClickHouse to calculate ping statistics with appropriate granularity
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

        // Cache the result for 20 minutes
        await _cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(20));
        _logger.LogDebug("Cached server insights: {ServerName}, period: {Period}", serverName, period);

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

    private async Task<List<PlayerCountDataPoint>> GetPlayerCountHistoryFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        // First, let's check if we have any data for this server at all
        var dataCheckQuery = $@"
SELECT COUNT(*) as total_records, 
       MIN(round_start_time) as earliest, 
       MAX(round_start_time) as latest,
       COUNT(DISTINCT player_name) as unique_players
FROM player_rounds 
WHERE server_guid = '{serverGuid.Replace("'", "''")}' 
FORMAT TabSeparated";

        _logger.LogDebug("=== DATA CHECK QUERY ===");
        _logger.LogDebug("QUERY:\n{Query}", dataCheckQuery);
        
        var dataCheckResult = await _clickHouseReader.ExecuteQueryAsync(dataCheckQuery);
        _logger.LogDebug("DATA CHECK RESULT:\n{Result}", dataCheckResult);

        var timeGrouping = GetClickHouseTimeGrouping(granularity);
        
        var query = $@"
SELECT 
    {timeGrouping} as timestamp,
    COUNT(DISTINCT player_name) as unique_players_started
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
    AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
    AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY timestamp
ORDER BY timestamp
FORMAT TabSeparated";

        _logger.LogDebug("=== PLAYER COUNT HISTORY QUERY ===");
        _logger.LogDebug("Server GUID: {ServerGuid}", serverGuid);
        _logger.LogDebug("Start Period: {StartPeriod}", startPeriod);
        _logger.LogDebug("End Period: {EndPeriod}", endPeriod);
        _logger.LogDebug("Granularity: {Granularity}", granularity);
        _logger.LogDebug("Time Grouping: {TimeGrouping}", timeGrouping);
        _logger.LogDebug("FULL QUERY:\n{Query}", query);
        
        var result = await _clickHouseReader.ExecuteQueryAsync(query);
        
        _logger.LogDebug("RAW RESULT:\n{Result}", result);
        _logger.LogDebug("Result length: {Length} characters", result?.Length ?? 0);
        _logger.LogDebug("Result lines: {Lines}", result?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length ?? 0);
        
        var history = new List<PlayerCountDataPoint>();

        var lineCount = 0;
        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
        {
            lineCount++;
            _logger.LogDebug("Processing line {LineNumber}: '{Line}'", lineCount, line);
            
            var parts = line.Split('\t');
            _logger.LogDebug("Split into {PartCount} parts: [{Parts}]", parts.Length, string.Join(", ", parts));
            
            if (parts.Length >= 2 && 
                DateTime.TryParse(parts[0], out var timestamp) && 
                int.TryParse(parts[1], out var playersStarted))
            {
                _logger.LogDebug("Successfully parsed: timestamp={Timestamp}, players={Players}", timestamp, playersStarted);
                history.Add(new PlayerCountDataPoint
                {
                    Timestamp = timestamp,
                    PlayerCount = playersStarted,
                    UniquePlayersStarted = playersStarted
                });
            }
            else
            {
                _logger.LogWarning("Failed to parse line: parts.Length={Length}, timestamp parse={TimestampParse}, players parse={PlayersStartedParse}", 
                    parts.Length, 
                    parts.Length > 0 ? DateTime.TryParse(parts[0], out _) : false,
                    parts.Length > 1 ? int.TryParse(parts[1], out _) : false);
            }
        }
        
        _logger.LogDebug("Final history count: {Count}", history.Count);

        return history;
    }

    private async Task<PlayerCountSummary> GetPlayerCountSummaryFromClickHouse(
        string serverGuid, DateTime startPeriod, DateTime endPeriod, TimeGranularity granularity)
    {
        var timeGrouping = GetClickHouseTimeGrouping(granularity);
        var totalDays = (int)(endPeriod - startPeriod).TotalDays;
        var halfPeriodDays = totalDays / 2;
        var midPeriod = startPeriod.AddDays(halfPeriodDays);

        var query = $@"
WITH time_buckets AS (
    SELECT 
        {timeGrouping} as time_bucket,
        COUNT(DISTINCT player_name) as players_in_bucket
    FROM player_rounds
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
    GROUP BY time_bucket
),
period_comparison AS (
    SELECT 
        CASE 
            WHEN round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}' 
                AND round_start_time < '{midPeriod:yyyy-MM-dd HH:mm:ss}' THEN 'older'
            WHEN round_start_time >= '{midPeriod:yyyy-MM-dd HH:mm:ss}' 
                AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}' THEN 'recent'
        END as period_type,
        COUNT(DISTINCT player_name) as unique_players
    FROM player_rounds
    WHERE server_guid = '{serverGuid.Replace("'", "''")}'
        AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
        AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}'
    GROUP BY period_type, toStartOfHour(round_start_time)
),
summary AS (
    SELECT 
        AVG(players_in_bucket) as avg_players,
        MAX(players_in_bucket) as peak_players,
        argMax(time_bucket, players_in_bucket) as peak_timestamp,
        (SELECT COUNT(DISTINCT player_name) FROM player_rounds 
         WHERE server_guid = '{serverGuid.Replace("'", "''")}'
           AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
           AND round_start_time < '{endPeriod:yyyy-MM-dd HH:mm:ss}') as total_unique
    FROM time_buckets
),
period_averages AS (
    SELECT 
        period_type,
        AVG(unique_players) as avg_players
    FROM period_comparison
    WHERE period_type IS NOT NULL
    GROUP BY period_type
)
SELECT 
    s.avg_players,
    s.peak_players,
    s.peak_timestamp,
    s.total_unique,
    COALESCE(
        ROUND((recent.avg_players - older.avg_players) / NULLIF(older.avg_players, 0) * 100),
        0
    ) as change_percent
FROM summary s
LEFT JOIN (SELECT avg_players FROM period_averages WHERE period_type = 'recent') recent ON 1=1
LEFT JOIN (SELECT avg_players FROM period_averages WHERE period_type = 'older') older ON 1=1
FORMAT TabSeparated";

        _logger.LogDebug("=== PLAYER COUNT SUMMARY QUERY ===");
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

}

public enum TimeGranularity
{
    Hourly,
    FourHourly,
    Daily,
    Weekly,
    Monthly
}