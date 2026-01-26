using api.Caching;
using api.GameTrends;
using api.Gamification.Models;
using api.PlayerStats;
using api.PlayerTracking;
using api.Servers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Servers;

public class ServerStatsService(
    PlayerTrackerDbContext dbContext,
    ILogger<ServerStatsService> logger,
    ICacheService cacheService,
    ICacheKeyService cacheKeyService,
    RoundsService roundsService,
    ISqliteGameTrendsService sqliteGameTrendsService,
    ISqliteLeaderboardService sqliteLeaderboardService) : IServerStatsService
{
    public async Task<ServerStatistics> GetServerStatistics(
        string serverName,
        int daysToAnalyze = 7)
    {
        // Check cache first - simplified cache key since we removed all leaderboard queries
        var cacheKey = cacheKeyService.GetServerStatisticsKey(serverName, daysToAnalyze);
        var cachedResult = await cacheService.GetAsync<ServerStatistics>(cacheKey);

        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name - CurrentMap is now stored directly on the server
        var server = await dbContext.Servers
            .Where(s => s.Name == serverName)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            logger.LogWarning("Server not found: '{ServerName}'", serverName);
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
        var recentRoundsTask = roundsService.GetRecentRoundsAsync(server.Guid, 8);

        // Get busy indicator data for this server
        try
        {
            var busyIndicatorData = await GetServerBusyIndicatorAsync(server.Guid);
            statistics.BusyIndicator = busyIndicatorData;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get busy indicator for server {ServerName} ({ServerGuid})", serverName, server.Guid);
            // Continue without busy indicator data rather than failing the entire request
        }

        statistics.RecentRounds = await recentRoundsTask;

        // Cache the result for 10 minutes
        await cacheService.SetAsync(cacheKey, statistics, TimeSpan.FromMinutes(10));
        logger.LogDebug("Cached server statistics: {ServerName}, {Days} days", serverName, daysToAnalyze);

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
        var cacheKey = $"{cacheKeyService.GetServerLeaderboardsKey(serverName, days)}_weight_{minPlayersForWeighting}";
        var cachedResult = await cacheService.GetAsync<ServerLeaderboards>(cacheKey);

        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server leaderboards: {ServerName}, {Days} days", serverName, days);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server leaderboards: {ServerName}, {Days} days", serverName, days);

        // Get the server by name
        var server = await dbContext.Servers
            .Where(s => s.Name == serverName)
            .FirstOrDefaultAsync();

        if (server == null)
        {
            logger.LogWarning("Server not found: '{ServerName}'", serverName);
            return new ServerLeaderboards
            {
                ServerName = serverName,
                Days = days
            };
        }

        // Calculate time range based on days
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-days);

        logger.LogInformation("Fetching leaderboards for {ServerName} ({ServerGuid}) from {StartPeriod} to {EndPeriod}",
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
            var mostActivePlayersTask = sqliteLeaderboardService.GetMostActivePlayersAsync(server.Guid, startPeriod, endPeriod, 10);
            var topScoresTask = sqliteLeaderboardService.GetTopScoresAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKDRatiosTask = sqliteLeaderboardService.GetTopKDRatiosAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKillRatesTask = sqliteLeaderboardService.GetTopKillRatesAsync(server.Guid, startPeriod, endPeriod, 10);
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

            logger.LogInformation("Leaderboards fetched: MostActive={MostActiveCount}, TopScores={TopScoresCount}, TopKD={TopKDCount}, TopKillRate={TopKillRateCount}, TopPlacements={TopPlacementsCount}",
                leaderboards.MostActivePlayersByTime.Count,
                leaderboards.TopScores.Count,
                leaderboards.TopKDRatios.Count,
                leaderboards.TopKillRates.Count,
                leaderboards.TopPlacements.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching leaderboards for server {ServerName} ({ServerGuid})", server.Name, server.Guid);
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

                logger.LogInformation("Weighted placements fetched: Count={Count}", leaderboards.WeightedTopPlacements?.Count ?? 0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching weighted placements for server {ServerName} ({ServerGuid})", server.Name, server.Guid);
            }
        }

        // Cache the result for 10 minutes
        await cacheService.SetAsync(cacheKey, leaderboards, TimeSpan.FromMinutes(10));
        logger.LogDebug("Cached server leaderboards: {ServerName}, {Days} days", serverName, days);

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
            var startInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(startPeriod, DateTimeKind.Utc));
            var endInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endPeriod, DateTimeKind.Utc));

            var results = await dbContext.PlayerAchievements
                .AsNoTracking()
                .Where(pa => pa.AchievementType == AchievementTypes.Placement
                             && pa.ServerGuid == serverGuid
                             && pa.AchievedAt >= startInstant
                             && pa.AchievedAt < endInstant)
                .GroupBy(pa => pa.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    FirstPlaces = g.Count(pa => pa.Tier == "gold"),
                    SecondPlaces = g.Count(pa => pa.Tier == "silver"),
                    ThirdPlaces = g.Count(pa => pa.Tier == "bronze")
                })
                .Where(x => x.FirstPlaces > 0 || x.SecondPlaces > 0 || x.ThirdPlaces > 0)
                .OrderByDescending(x => x.FirstPlaces)
                .ThenByDescending(x => x.SecondPlaces)
                .ThenByDescending(x => x.ThirdPlaces)
                .Take(limit)
                .ToListAsync();

            return results.Select((entry, index) => new PlacementLeaderboardEntry
            {
                Rank = index + 1,
                PlayerName = entry.PlayerName,
                FirstPlaces = entry.FirstPlaces,
                SecondPlaces = entry.SecondPlaces,
                ThirdPlaces = entry.ThirdPlaces
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching placement leaderboard for server {ServerGuid}", serverGuid);
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
            var startInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(startPeriod, DateTimeKind.Utc));
            var endInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endPeriod, DateTimeKind.Utc));

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    PlayerName,
    SUM(CASE WHEN Tier = 'gold' THEN 1 ELSE 0 END) AS FirstPlaces,
    SUM(CASE WHEN Tier = 'silver' THEN 1 ELSE 0 END) AS SecondPlaces,
    SUM(CASE WHEN Tier = 'bronze' THEN 1 ELSE 0 END) AS ThirdPlaces
FROM PlayerAchievements
WHERE AchievementType = $achievementType
  AND ServerGuid = $serverGuid
  AND AchievedAt >= $startInstant
  AND AchievedAt < $endInstant
  AND COALESCE(CAST(json_extract(Metadata, '$.TotalPlayers') AS INTEGER), 0) >= $minPlayerCount
GROUP BY PlayerName
HAVING FirstPlaces > 0 OR SecondPlaces > 0 OR ThirdPlaces > 0
ORDER BY FirstPlaces DESC, SecondPlaces DESC, ThirdPlaces DESC
LIMIT $limit";

            var pAchievementType = command.CreateParameter();
            pAchievementType.ParameterName = "$achievementType";
            pAchievementType.Value = AchievementTypes.Placement;
            command.Parameters.Add(pAchievementType);

            var pServerGuid = command.CreateParameter();
            pServerGuid.ParameterName = "$serverGuid";
            pServerGuid.Value = serverGuid;
            command.Parameters.Add(pServerGuid);

            var pStartInstant = command.CreateParameter();
            pStartInstant.ParameterName = "$startInstant";
            pStartInstant.Value = startInstant.ToString();
            command.Parameters.Add(pStartInstant);

            var pEndInstant = command.CreateParameter();
            pEndInstant.ParameterName = "$endInstant";
            pEndInstant.Value = endInstant.ToString();
            command.Parameters.Add(pEndInstant);

            var pMinPlayerCount = command.CreateParameter();
            pMinPlayerCount.ParameterName = "$minPlayerCount";
            pMinPlayerCount.Value = minPlayerCount;
            command.Parameters.Add(pMinPlayerCount);

            var pLimit = command.CreateParameter();
            pLimit.ParameterName = "$limit";
            pLimit.Value = limit;
            command.Parameters.Add(pLimit);

            var entries = new List<PlacementLeaderboardEntry>();
            await using var reader = await command.ExecuteReaderAsync();
            var rank = 1;
            while (await reader.ReadAsync())
            {
                entries.Add(new PlacementLeaderboardEntry
                {
                    Rank = rank++,
                    PlayerName = reader.GetString(0),
                    FirstPlaces = reader.GetInt32(1),
                    SecondPlaces = reader.GetInt32(2),
                    ThirdPlaces = reader.GetInt32(3)
                });
            }

            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching placement leaderboard for server {ServerGuid}", serverGuid);
            return new List<PlacementLeaderboardEntry>();
        }
    }

    // Removed legacy last rounds method that depended on HistoricalRoundsService






    public async Task<ServerInsights> GetServerInsights(string serverName, int days = 7, int? rollingWindowDays = null)
    {
        // Validate days parameter
        if (days <= 0)
            throw new ArgumentException("Days must be greater than 0", nameof(days));

        // Calculate time periods and granularity based on days
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-days);
        var granularity = CalculateGranularity(days);

        // Check cache first
        var cacheKey = cacheKeyService.GetServerInsightsKey(serverName, days, rollingWindowDays);
        var cachedResult = await cacheService.GetAsync<ServerInsights>(cacheKey);

        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server insights: {ServerName}, days: {Days}, rollingWindowDays: {RollingWindowDays}", serverName, days, rollingWindowDays);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server insights: {ServerName}, days: {Days}, rollingWindowDays: {RollingWindowDays}", serverName, days, rollingWindowDays);

        // Get the server by name
        var server = await dbContext.Servers
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

        // Use provided rollingWindowDays if specified, otherwise use calculated value
        if (rollingWindowDays.HasValue && rollingWindowDays.Value > 0)
        {
            rollingWindow = rollingWindowDays.Value;
        }

        // Fetch players online history
        try
        {
            insights.PlayersOnlineHistory = await sqliteGameTrendsService.GetPlayersOnlineHistoryAsync(
                server.GameId, period, rollingWindow, server.Guid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching players online history");
            insights.PlayersOnlineHistory = null;
        }

        // Cache the result for 20 minutes
        await cacheService.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(20));

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
        var cacheKey = cacheKeyService.GetServerMapsInsightsKey(serverName, days);
        var cachedResult = await cacheService.GetAsync<ServerMapsInsights>(cacheKey);

        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server maps insights: {ServerName}, days: {Days}", serverName, days);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server maps insights: {ServerName}, days: {Days}", serverName, days);

        // Get the server by name
        var server = await dbContext.Servers
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
            mapsInsights.Maps = await GetAllMapsFromSqlite(server.Guid, startPeriod, endPeriod);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching maps data");
            mapsInsights.Maps = [];
        }

        // Cache the result for 20 minutes
        await cacheService.SetAsync(cacheKey, mapsInsights, TimeSpan.FromMinutes(20));

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

    /// <summary>
    /// Gets map statistics from SQLite ServerMapStats aggregate table.
    /// Aggregates monthly buckets within the date range.
    /// </summary>
    private async Task<List<PopularMapDataPoint>> GetAllMapsFromSqlite(
        string serverGuid, DateTime startPeriod, DateTime endPeriod)
    {
        // Calculate year/month bounds for filtering
        var startYear = startPeriod.Year;
        var startMonth = startPeriod.Month;
        var endYear = endPeriod.Year;
        var endMonth = endPeriod.Month;

        // Query and aggregate across months
        var mapStats = await dbContext.ServerMapStats
            .AsNoTracking()
            .Where(sms => sms.ServerGuid == serverGuid
                && ((sms.Year > startYear || (sms.Year == startYear && sms.Month >= startMonth))
                && (sms.Year < endYear || (sms.Year == endYear && sms.Month <= endMonth))))
            .GroupBy(sms => sms.MapName)
            .Select(g => new
            {
                MapName = g.Key,
                TotalRounds = g.Sum(sms => sms.TotalRounds),
                TotalPlayTimeMinutes = g.Sum(sms => sms.TotalPlayTimeMinutes),
                // Weighted average of concurrent players by rounds
                AvgConcurrentPlayers = g.Sum(sms => sms.AvgConcurrentPlayers * sms.TotalRounds) /
                    Math.Max(g.Sum(sms => sms.TotalRounds), 1),
                PeakConcurrentPlayers = g.Max(sms => sms.PeakConcurrentPlayers),
                Team1Victories = g.Sum(sms => sms.Team1Victories),
                Team2Victories = g.Sum(sms => sms.Team2Victories),
                // Pick any non-null label (they should be consistent per server/map)
                Team1Label = g.Select(sms => sms.Team1Label).FirstOrDefault(l => l != null),
                Team2Label = g.Select(sms => sms.Team2Label).FirstOrDefault(l => l != null)
            })
            .OrderByDescending(m => m.TotalPlayTimeMinutes)
            .ThenByDescending(m => m.AvgConcurrentPlayers)
            .ToListAsync();

        // Calculate total play time for percentage
        var totalPlayTime = mapStats.Sum(m => m.TotalPlayTimeMinutes);

        var result = mapStats.Select(m => new PopularMapDataPoint
        {
            MapName = m.MapName,
            AveragePlayerCount = Math.Round(m.AvgConcurrentPlayers, 2),
            PeakPlayerCount = m.PeakConcurrentPlayers,
            TotalPlayTime = m.TotalPlayTimeMinutes,
            PlayTimePercentage = totalPlayTime > 0
                ? Math.Round(m.TotalPlayTimeMinutes * 100.0 / totalPlayTime, 2)
                : 0,
            Team1Victories = m.Team1Victories,
            Team2Victories = m.Team2Victories,
            Team1Label = m.Team1Label,
            Team2Label = m.Team2Label
        }).ToList();

        logger.LogDebug("SQLite maps query returned {Count} maps for server {ServerGuid}", result.Count, serverGuid);

        return result;
    }

    public async Task<PagedResult<ServerBasicInfo>> GetAllServersWithPaging(
        int page = 1,
        int pageSize = 50,
        string sortBy = "ServerName",
        string sortOrder = "asc",
        ServerFilters? filters = null)
    {
        // Check cache first
        var cacheKey = cacheKeyService.GetServersPageKey(page, pageSize, sortBy, sortOrder, filters);
        var cachedResult = await cacheService.GetAsync<PagedResult<ServerBasicInfo>>(cacheKey);

        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server search: page {Page}, pageSize {PageSize}", page, pageSize);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server search: page {Page}, pageSize {PageSize}", page, pageSize);

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
        IQueryable<GameServer> baseQuery = dbContext.Servers.AsNoTracking();

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
        await cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        logger.LogDebug("Cached server search results: page {Page}, pageSize {PageSize}", page, pageSize);

        return result;
    }

    /// <summary>
    /// Get busy indicator data for a single server with 8 hours before/after forecast timeline
    /// </summary>
    private async Task<ServerBusyIndicator> GetServerBusyIndicatorAsync(string serverGuid)
    {
        // Get busy indicator data for this single server with 8 hours before/after
        var busyIndicatorResult = await sqliteGameTrendsService.GetServerBusyIndicatorAsync(new[] { serverGuid }, timelineHourRange: 8);
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
                HourlyTimeline = new List<HourlyBusyData>(),
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
                    new HistoricalRange
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
            HourlyTimeline = serverResult.HourlyTimeline?.Select(ht => new HourlyBusyData
            {
                Hour = ht.Hour,
                TypicalPlayers = ht.TypicalPlayers,
                BusyLevel = ht.BusyLevel,
                IsCurrentHour = ht.IsCurrentHour
            }).ToList() ?? new List<HourlyBusyData>(),
            GeneratedAt = busyIndicatorResult.GeneratedAt
        };

        return busyIndicator;
    }

    /// <summary>
    /// Get server rankings by total playtime for the last N days
    /// </summary>
    public async Task<List<ServerRank>> GetServerRankingsByPlaytimeAsync(
        IEnumerable<string> serverGuids,
        int days = 30)
    {
        var serverGuidList = serverGuids.ToList();
        if (!serverGuidList.Any())
        {
            return new List<ServerRank>();
        }

        // Check cache first
        var cacheKey = cacheKeyService.GetServerPlaytimeRankingsKey(serverGuidList, days);
        var cachedResult = await cacheService.GetAsync<List<ServerRank>>(cacheKey);
        if (cachedResult != null)
        {
            logger.LogDebug("Cache hit for server rankings: {ServerCount} servers, {Days} days", serverGuidList.Count, days);
            return cachedResult;
        }

        logger.LogDebug("Cache miss for server rankings: {ServerCount} servers, {Days} days", serverGuidList.Count, days);

        // Calculate the start date (last N days ago)
        var startDate = DateTime.UtcNow.AddDays(-days);

        // Get the ISO week boundary for the start date
        var startIsoWeek = GetIsoWeek(startDate);
        var startIsoYear = startDate.Year;

        // Query PlayerServerStats to sum playtime per server from the start date onwards
        // Include all records from the week containing the start date up to the latest available data
        var serverPlaytimeTotals = await dbContext.PlayerServerStats
            .Where(pss => serverGuidList.Contains(pss.ServerGuid))
            .Where(pss =>
                // Include records where year > start year, or year == start year and week >= start week
                (pss.Year > startIsoYear) ||
                (pss.Year == startIsoYear && pss.Week >= startIsoWeek))
            .GroupBy(pss => pss.ServerGuid)
            .Select(g => new
            {
                ServerGuid = g.Key,
                TotalPlayTimeMinutes = g.Sum(pss => pss.TotalPlayTimeMinutes)
            })
            .Where(x => x.TotalPlayTimeMinutes > 0) // Only include servers with playtime
            .OrderByDescending(x => x.TotalPlayTimeMinutes)
            .ToListAsync();

        // Create rankings
        var rankings = serverPlaytimeTotals
            .Select((server, index) => new ServerRank
            {
                ServerGuid = server.ServerGuid,
                Rank = index + 1,
                TotalPlayTimeMinutes = server.TotalPlayTimeMinutes
            })
            .ToList();

        // Include servers with no playtime data at the end
        var serversWithData = new HashSet<string>(rankings.Select(r => r.ServerGuid));
        var serversWithoutData = serverGuidList
            .Where(guid => !serversWithData.Contains(guid))
            .Select((guid, index) => new ServerRank
            {
                ServerGuid = guid,
                Rank = rankings.Count + index + 1,
                TotalPlayTimeMinutes = 0
            });

        rankings.AddRange(serversWithoutData);

        // Cache the result for 15 minutes (shorter than server stats since rankings can change more frequently)
        await cacheService.SetAsync(cacheKey, rankings, TimeSpan.FromMinutes(15));
        logger.LogDebug("Cached server rankings: {ServerCount} servers, {Days} days", serverGuidList.Count, days);

        return rankings;
    }

    /// <summary>
    /// Get ISO week number for a given date
    /// </summary>
    private static int GetIsoWeek(DateTime date)
    {
        var day = (int)System.Globalization.CultureInfo.CurrentCulture.Calendar.GetDayOfWeek(date);
        return System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }

}
