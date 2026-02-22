using api.DataExplorer.Models;
using api.Gamification.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.DataExplorer;

/// <summary>
/// Data Explorer service implementation.
/// Uses raw SQL aggregation on existing tables (ServerMapStats, PlayerMapStats)
/// instead of loading data into memory. Leverages year/month bucketing for time-slicing.
/// Reads server online status from the Servers table instead of calling external APIs.
/// </summary>
public class DataExplorerService(
    PlayerTrackerDbContext dbContext,
    ILogger<DataExplorerService> logger) : IDataExplorerService
{
    private const int Last30Days = 30;

    /// <summary>
    /// Valid game types for filtering.
    /// </summary>
    private static readonly HashSet<string> ValidGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bf1942", "fh2", "bfvietnam"
    };

    /// <summary>
    /// Normalize game parameter to lowercase, defaulting to bf1942 if invalid.
    /// </summary>
    private static string NormalizeGame(string? game) =>
        !string.IsNullOrWhiteSpace(game) && ValidGames.Contains(game)
            ? game.ToLowerInvariant()
            : "bf1942";

    public async Task<ServerListResponse> GetServersAsync(string game = "bf1942", int page = 1, int pageSize = 50)
    {
        var normalizedGame = NormalizeGame(game);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get servers for the specified game from the database
        // The IsOnline flag is maintained by the background job that polls the BfList API
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => new { s.Guid, s.Name, s.Game, s.Country, s.MaxPlayers, s.CurrentNumPlayers, s.IsOnline })
            .ToListAsync();

        if (servers.Count == 0)
            return new ServerListResponse([], 0, page, pageSize, false);

        // Use raw SQL to aggregate ServerMapStats for last 30 days - fully computed in SQLite
        // Filter by game through the server GUIDs
        var cutoffDate = DateTime.UtcNow.AddDays(-Last30Days);
        var cutoffYear = cutoffDate.Year;
        var cutoffMonth = cutoffDate.Month;

        var serverGuids = servers.Select(s => s.Guid).ToList();

        // Build parameterized IN clause for server GUIDs
        var guidParams = string.Join(", ", serverGuids.Select((_, i) => $"@p{i + 2}"));
        var serverStatsSql = $@"
            SELECT 
                ServerGuid,
                COUNT(DISTINCT MapName) as TotalMaps,
                SUM(TotalRounds) as TotalRounds
            FROM ServerMapStats
            WHERE ((Year > @p0) OR (Year = @p0 AND Month >= @p1))
              AND ServerGuid IN ({guidParams})
            GROUP BY ServerGuid";

        var sqlParams = new List<object> { cutoffYear, cutoffMonth };
        sqlParams.AddRange(serverGuids.Cast<object>());

        var serverStats = await dbContext.Database
            .SqlQueryRaw<ServerStatsQueryResult>(serverStatsSql, sqlParams.ToArray())
            .ToListAsync();

        var statsDict = serverStats.ToDictionary(x => x.ServerGuid);

        var allServerSummaries = servers.Select(s =>
        {
            statsDict.TryGetValue(s.Guid, out var stats);

            return new ServerSummaryDto(
                Guid: s.Guid,
                Name: s.Name,
                Game: s.Game,
                Country: s.Country,
                IsOnline: s.IsOnline,
                CurrentPlayers: s.CurrentNumPlayers, // Current players from database field
                MaxPlayers: s.MaxPlayers ?? 0,
                TotalMaps: stats?.TotalMaps ?? 0,
                TotalRoundsLast30Days: stats?.TotalRounds ?? 0
            );
        })
        .OrderByDescending(s => s.IsOnline)
        .ThenByDescending(s => s.CurrentPlayers) // Sort by current active players
        .ThenByDescending(s => s.TotalRoundsLast30Days)
        .ThenBy(s => s.Name)
        .ToList();

        var totalCount = allServerSummaries.Count;
        var skip = (page - 1) * pageSize;
        var paginatedServers = allServerSummaries
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        var hasMore = skip + paginatedServers.Count < totalCount;

        return new ServerListResponse(paginatedServers, totalCount, page, pageSize, hasMore);
    }

    public async Task<ServerDetailDto?> GetServerDetailAsync(string serverGuid)
    {
        var server = await dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Guid == serverGuid);

        if (server == null)
            return null;

        // Use the IsOnline flag from the database (maintained by background job)
        var isOnline = server.IsOnline;

        // Use raw SQL to aggregate map rotation data with window function for percentage
        // All computation happens in SQLite - no in-memory grouping
        var mapRotationSql = @"
            SELECT
                MapName,
                TotalRounds,
                TotalPlayTimeMinutes,
                CASE WHEN ServerTotalPlayTime > 0 
                     THEN ROUND(100.0 * TotalPlayTimeMinutes / ServerTotalPlayTime, 1) 
                     ELSE 0 END as PlayTimePercentage,
                ROUND(AvgConcurrentPlayers, 1) as AvgConcurrentPlayers,
                Team1Victories,
                Team2Victories,
                CASE WHEN (Team1Victories + Team2Victories) > 0 
                     THEN ROUND(100.0 * Team1Victories / (Team1Victories + Team2Victories), 1) 
                     ELSE 0 END as Team1WinPercentage,
                CASE WHEN (Team1Victories + Team2Victories) > 0 
                     THEN ROUND(100.0 * Team2Victories / (Team1Victories + Team2Victories), 1) 
                     ELSE 0 END as Team2WinPercentage,
                Team1Label,
                Team2Label
            FROM (
                SELECT
                    MapName,
                    SUM(TotalRounds) as TotalRounds,
                    SUM(TotalPlayTimeMinutes) as TotalPlayTimeMinutes,
                    AVG(AvgConcurrentPlayers) as AvgConcurrentPlayers,
                    SUM(Team1Victories) as Team1Victories,
                    SUM(Team2Victories) as Team2Victories,
                    MAX(Team1Label) as Team1Label,
                    MAX(Team2Label) as Team2Label,
                    SUM(SUM(TotalPlayTimeMinutes)) OVER () as ServerTotalPlayTime
                FROM ServerMapStats
                WHERE ServerGuid = @p0
                GROUP BY MapName
            )
            ORDER BY PlayTimePercentage DESC";

        var mapRotationData = await dbContext.Database
            .SqlQueryRaw<MapRotationQueryResult>(mapRotationSql, serverGuid)
            .ToListAsync();

        var topWinnerByMap = await GetTopPlacementWinnersByMapAsync(
            serverGuid,
            mapRotationData.Select(m => m.MapName).ToList()
        );

        var mapRotation = mapRotationData.Select(m => new MapRotationItemDto(
            MapName: m.MapName,
            TotalRounds: m.TotalRounds,
            PlayTimePercentage: m.PlayTimePercentage,
            AvgConcurrentPlayers: m.AvgConcurrentPlayers,
            WinStats: new WinStatsDto(
                Team1Label: m.Team1Label ?? "Team 1",
                Team2Label: m.Team2Label ?? "Team 2",
                Team1Victories: m.Team1Victories,
                Team2Victories: m.Team2Victories,
                Team1WinPercentage: m.Team1WinPercentage,
                Team2WinPercentage: m.Team2WinPercentage,
                TotalRounds: m.TotalRounds
            ),
            TopPlayerByWins: topWinnerByMap.TryGetValue(m.MapName, out var winner) ? winner : null
        )).ToList();

        // Calculate overall win stats
        var overallTeam1Victories = mapRotationData.Sum(x => x.Team1Victories);
        var overallTeam2Victories = mapRotationData.Sum(x => x.Team2Victories);
        var overallTotalRounds = mapRotationData.Sum(x => x.TotalRounds);
        var overallTotalWins = overallTeam1Victories + overallTeam2Victories;
        var overallTeam1Label = mapRotationData.FirstOrDefault(x => !string.IsNullOrEmpty(x.Team1Label))?.Team1Label ?? "Team 1";
        var overallTeam2Label = mapRotationData.FirstOrDefault(x => !string.IsNullOrEmpty(x.Team2Label))?.Team2Label ?? "Team 2";

        var overallWinStats = new WinStatsDto(
            Team1Label: overallTeam1Label,
            Team2Label: overallTeam2Label,
            Team1Victories: overallTeam1Victories,
            Team2Victories: overallTeam2Victories,
            Team1WinPercentage: overallTotalWins > 0 ? Math.Round(100.0 * overallTeam1Victories / overallTotalWins, 1) : 0,
            Team2WinPercentage: overallTotalWins > 0 ? Math.Round(100.0 * overallTeam2Victories / overallTotalWins, 1) : 0,
            TotalRounds: overallTotalRounds
        );

        // Get top 5 players per map using a single query with window function (ROW_NUMBER)
        // This replaces the N+1 query pattern - queries PlayerMapStats directly
        var topMapNames = mapRotationData.Take(10).Select(m => m.MapName).ToList();

        // Build parameterized IN clause
        var mapParams = string.Join(", ", topMapNames.Select((_, i) => $"@p{i + 1}"));
        var topPlayersSql = $@"
            SELECT MapName, PlayerName, TotalScore, TotalKills, TotalDeaths, KdRatio, Rank
            FROM (
                SELECT
                    MapName,
                    PlayerName,
                    SUM(TotalScore) as TotalScore,
                    SUM(TotalKills) as TotalKills,
                    SUM(TotalDeaths) as TotalDeaths,
                    CASE WHEN SUM(TotalDeaths) > 0 
                         THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths), 2) 
                         ELSE SUM(TotalKills) END as KdRatio,
                    ROW_NUMBER() OVER (PARTITION BY MapName ORDER BY SUM(TotalScore) DESC) as Rank
                FROM PlayerMapStats
                WHERE ServerGuid = @p0 AND MapName IN ({mapParams})
                GROUP BY MapName, PlayerName
            )
            WHERE Rank <= 5
            ORDER BY MapName, Rank";

        var sqlParams = new List<object> { serverGuid };
        sqlParams.AddRange(topMapNames.Cast<object>());

        var topPlayersData = await dbContext.Database
            .SqlQueryRaw<TopPlayerQueryResult>(topPlayersSql, sqlParams.ToArray())
            .ToListAsync();

        var topPlayersByMap = topPlayersData
            .GroupBy(tp => tp.MapName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var perMapStats = topMapNames.Select(mapName =>
        {
            var mapWinStats = mapRotation.FirstOrDefault(x => x.MapName == mapName)?.WinStats
                ?? new WinStatsDto("Team 1", "Team 2", 0, 0, 0, 0, 0);

            var players = topPlayersByMap.TryGetValue(mapName, out var tp) ? tp : [];

            return new PerMapStatsDto(
                MapName: mapName,
                WinStats: mapWinStats,
                TopPlayers: players.Select(p => new TopPlayerDto(
                    PlayerName: p.PlayerName,
                    TotalScore: p.TotalScore,
                    TotalKills: p.TotalKills,
                    KdRatio: p.KdRatio
                )).ToList()
            );
        }).ToList();

        // Get activity patterns (already uses aggregate table - ServerHourlyPatterns)
        var activityPatternsData = await dbContext.ServerHourlyPatterns
            .AsNoTracking()
            .Where(shp => shp.ServerGuid == serverGuid)
            .ToListAsync();

        var activityPatterns = activityPatternsData
            .Select(shp => new ActivityPatternDto(
                shp.DayOfWeek,
                shp.HourOfDay,
                Math.Round(shp.AvgPlayers, 1),
                Math.Round(shp.MedianPlayers, 1)
            ))
            .ToList();

        return new ServerDetailDto(
            Guid: server.Guid,
            Name: server.Name,
            Game: server.Game,
            Country: server.Country,
            IsOnline: isOnline,
            MapRotation: mapRotation,
            OverallWinStats: overallWinStats,
            PerMapStats: perMapStats,
            ActivityPatterns: activityPatterns
        );
    }

    public async Task<MapRotationResponse?> GetServerMapRotationAsync(string serverGuid, int page = 1, int pageSize = 10, int days = 60)
    {
        // Verify server exists
        var server = await dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Guid == serverGuid);

        if (server == null)
            return null;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Use raw SQL to aggregate map rotation data with window function for percentage
        // All computation happens in SQLite - no in-memory grouping
        var mapRotationSql = @"
            SELECT
                MapName,
                TotalRounds,
                TotalPlayTimeMinutes,
                CASE WHEN ServerTotalPlayTime > 0 
                     THEN ROUND(100.0 * TotalPlayTimeMinutes / ServerTotalPlayTime, 1) 
                     ELSE 0 END as PlayTimePercentage,
                ROUND(AvgConcurrentPlayers, 1) as AvgConcurrentPlayers,
                Team1Victories,
                Team2Victories,
                CASE WHEN (Team1Victories + Team2Victories) > 0 
                     THEN ROUND(100.0 * Team1Victories / (Team1Victories + Team2Victories), 1) 
                     ELSE 0 END as Team1WinPercentage,
                CASE WHEN (Team1Victories + Team2Victories) > 0 
                     THEN ROUND(100.0 * Team2Victories / (Team1Victories + Team2Victories), 1) 
                     ELSE 0 END as Team2WinPercentage,
                Team1Label,
                Team2Label
            FROM (
                SELECT
                    MapName,
                    SUM(TotalRounds) as TotalRounds,
                    SUM(TotalPlayTimeMinutes) as TotalPlayTimeMinutes,
                    AVG(AvgConcurrentPlayers) as AvgConcurrentPlayers,
                    SUM(Team1Victories) as Team1Victories,
                    SUM(Team2Victories) as Team2Victories,
                    MAX(Team1Label) as Team1Label,
                    MAX(Team2Label) as Team2Label,
                    SUM(SUM(TotalPlayTimeMinutes)) OVER () as ServerTotalPlayTime
                FROM ServerMapStats
                WHERE ServerGuid = @p0
                  AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
                GROUP BY MapName
            )
            ORDER BY PlayTimePercentage DESC";

        var mapRotationData = await dbContext.Database
            .SqlQueryRaw<MapRotationQueryResult>(mapRotationSql, serverGuid, cutoffYear, cutoffMonth)
            .ToListAsync();

        var totalCount = mapRotationData.Count;
        var skip = (page - 1) * pageSize;
        var paginatedData = mapRotationData
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        var topWinnerByMap = await GetTopPlacementWinnersByMapAsync(
            serverGuid,
            paginatedData.Select(m => m.MapName).ToList()
        );

        var mapRotation = paginatedData.Select(m => new MapRotationItemDto(
            MapName: m.MapName,
            TotalRounds: m.TotalRounds,
            PlayTimePercentage: m.PlayTimePercentage,
            AvgConcurrentPlayers: m.AvgConcurrentPlayers,
            WinStats: new WinStatsDto(
                Team1Label: m.Team1Label ?? "Team 1",
                Team2Label: m.Team2Label ?? "Team 2",
                Team1Victories: m.Team1Victories,
                Team2Victories: m.Team2Victories,
                Team1WinPercentage: m.Team1WinPercentage,
                Team2WinPercentage: m.Team2WinPercentage,
                TotalRounds: m.TotalRounds
            ),
            TopPlayerByWins: topWinnerByMap.TryGetValue(m.MapName, out var winner) ? winner : null
        )).ToList();

        var hasMore = skip + paginatedData.Count < totalCount;

        return new MapRotationResponse(mapRotation, totalCount, page, pageSize, hasMore);
    }

    private async Task<Dictionary<string, TopMapWinnerDto>> GetTopPlacementWinnersByMapAsync(
        string serverGuid,
        List<string> mapNames)
    {
        var uniqueMaps = mapNames
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (uniqueMaps.Count == 0)
            return new Dictionary<string, TopMapWinnerDto>(StringComparer.Ordinal);

        var mapParams = string.Join(", ", uniqueMaps.Select((_, i) => $"@p{i + 2}"));
        var sql = $@"
            SELECT MapName, PlayerName, Wins
            FROM (
                SELECT
                    MapName,
                    PlayerName,
                    COUNT(*) as Wins,
                    ROW_NUMBER() OVER (
                        PARTITION BY MapName
                        ORDER BY COUNT(*) DESC, PlayerName ASC
                    ) as Rank
                FROM PlayerAchievements
                WHERE ServerGuid = @p0
                  AND AchievementType = @p1
                  AND Tier = 'gold'
                  AND MapName IN ({mapParams})
                GROUP BY MapName, PlayerName
            )
            WHERE Rank = 1";

        var sqlParams = new List<object> { serverGuid, AchievementTypes.Placement };
        sqlParams.AddRange(uniqueMaps.Cast<object>());

        var results = await dbContext.Database
            .SqlQueryRaw<MapTopWinnerQueryResult>(sql, sqlParams.ToArray())
            .ToListAsync();

        return results.ToDictionary(
            r => r.MapName,
            r => new TopMapWinnerDto(r.PlayerName, r.Wins),
            StringComparer.Ordinal
        );
    }

    public async Task<MapListResponse> GetMapsAsync(string game = "bf1942")
    {
        var normalizedGame = NormalizeGame(game);
        var cutoffDate = DateTime.UtcNow.AddDays(-Last30Days);
        var cutoffYear = cutoffDate.Year;
        var cutoffMonth = cutoffDate.Month;

        // Get server GUIDs for the specified game
        var serverGuids = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => s.Guid)
            .ToListAsync();

        if (serverGuids.Count == 0)
            return new MapListResponse([], 0);

        // Build parameterized IN clause for server GUIDs
        var guidParams = string.Join(", ", serverGuids.Select((_, i) => $"@p{i + 2}"));

        // Use raw SQL to aggregate ServerMapStats by map - fully computed in SQLite
        // Filter by game through the server GUIDs
        var mapStatsSql = $@"
            SELECT 
                MapName,
                COUNT(DISTINCT ServerGuid) as ServersPlayingCount,
                SUM(TotalRounds) as TotalRoundsLast30Days,
                ROUND(AVG(AvgConcurrentPlayers), 1) as AvgPlayersWhenPlayed
            FROM ServerMapStats
            WHERE ((Year > @p0) OR (Year = @p0 AND Month >= @p1))
              AND ServerGuid IN ({guidParams})
            GROUP BY MapName
            ORDER BY TotalRoundsLast30Days DESC";

        var sqlParams = new List<object> { cutoffYear, cutoffMonth };
        sqlParams.AddRange(serverGuids.Cast<object>());

        var mapStats = await dbContext.Database
            .SqlQueryRaw<MapStatsQueryResult>(mapStatsSql, sqlParams.ToArray())
            .ToListAsync();

        var result = mapStats.Select(m => new MapSummaryDto(
            m.MapName,
            m.ServersPlayingCount,
            m.TotalRoundsLast30Days,
            m.AvgPlayersWhenPlayed
        )).ToList();

        return new MapListResponse(result, result.Count);
    }

    public async Task<MapDetailDto?> GetMapDetailAsync(string mapName, string game = "bf1942")
    {
        var normalizedGame = NormalizeGame(game);

        // Get server GUIDs for the specified game
        var gameServerGuids = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => s.Guid)
            .ToListAsync();

        if (gameServerGuids.Count == 0)
            return null;

        // Build parameterized IN clause for server GUIDs
        var guidParams = string.Join(", ", gameServerGuids.Select((_, i) => $"@p{i + 1}"));

        // Use raw SQL to aggregate ServerMapStats by server for this map, filtered by game
        var serverStatsSql = $@"
            SELECT
                ServerGuid,
                SUM(TotalRounds) as TotalRounds,
                SUM(Team1Victories) as Team1Victories,
                SUM(Team2Victories) as Team2Victories,
                CASE WHEN SUM(Team1Victories) + SUM(Team2Victories) > 0 
                     THEN ROUND(100.0 * SUM(Team1Victories) / (SUM(Team1Victories) + SUM(Team2Victories)), 1) 
                     ELSE 0 END as Team1WinPercentage,
                CASE WHEN SUM(Team1Victories) + SUM(Team2Victories) > 0 
                     THEN ROUND(100.0 * SUM(Team2Victories) / (SUM(Team1Victories) + SUM(Team2Victories)), 1) 
                     ELSE 0 END as Team2WinPercentage,
                MAX(Team1Label) as Team1Label,
                MAX(Team2Label) as Team2Label
            FROM ServerMapStats
            WHERE MapName = @p0
              AND ServerGuid IN ({guidParams})
            GROUP BY ServerGuid
            ORDER BY TotalRounds DESC";

        var sqlParams = new List<object> { mapName };
        sqlParams.AddRange(gameServerGuids.Cast<object>());

        var serverStatsData = await dbContext.Database
            .SqlQueryRaw<ServerOnMapQueryResult>(serverStatsSql, sqlParams.ToArray())
            .ToListAsync();

        if (serverStatsData.Count == 0)
            return null;

        // Get server info (including IsOnline status from database)
        var serverGuids = serverStatsData.Select(x => x.ServerGuid).ToList();
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid);

        // Build server list using IsOnline from the database
        var serverList = serverStatsData
            .Select(ssd =>
            {
                servers.TryGetValue(ssd.ServerGuid, out var server);

                return new ServerOnMapDto(
                    ServerGuid: ssd.ServerGuid,
                    ServerName: server?.Name ?? "Unknown Server",
                    Game: server?.Game ?? normalizedGame,
                    IsOnline: server?.IsOnline ?? false,
                    TotalRoundsOnMap: ssd.TotalRounds,
                    WinStats: new WinStatsDto(
                        Team1Label: ssd.Team1Label ?? "Team 1",
                        Team2Label: ssd.Team2Label ?? "Team 2",
                        Team1Victories: ssd.Team1Victories,
                        Team2Victories: ssd.Team2Victories,
                        Team1WinPercentage: ssd.Team1WinPercentage,
                        Team2WinPercentage: ssd.Team2WinPercentage,
                        TotalRounds: ssd.TotalRounds
                    )
                );
            })
            .OrderByDescending(x => x.IsOnline)
            .ThenByDescending(x => x.TotalRoundsOnMap)
            .ToList();

        // Calculate aggregated win stats
        var totalTeam1Victories = serverStatsData.Sum(x => x.Team1Victories);
        var totalTeam2Victories = serverStatsData.Sum(x => x.Team2Victories);
        var totalRoundsAll = serverStatsData.Sum(x => x.TotalRounds);
        var totalWinsAll = totalTeam1Victories + totalTeam2Victories;
        var aggTeam1Label = serverStatsData.FirstOrDefault(x => !string.IsNullOrEmpty(x.Team1Label))?.Team1Label ?? "Team 1";
        var aggTeam2Label = serverStatsData.FirstOrDefault(x => !string.IsNullOrEmpty(x.Team2Label))?.Team2Label ?? "Team 2";

        var aggregatedWinStats = new WinStatsDto(
            Team1Label: aggTeam1Label,
            Team2Label: aggTeam2Label,
            Team1Victories: totalTeam1Victories,
            Team2Victories: totalTeam2Victories,
            Team1WinPercentage: totalWinsAll > 0 ? Math.Round(100.0 * totalTeam1Victories / totalWinsAll, 1) : 0,
            Team2WinPercentage: totalWinsAll > 0 ? Math.Round(100.0 * totalTeam2Victories / totalWinsAll, 1) : 0,
            TotalRounds: totalRoundsAll
        );

        return new MapDetailDto(
            MapName: mapName,
            Servers: serverList,
            AggregatedWinStats: aggregatedWinStats
        );
    }


    public async Task<ServerMapDetailDto?> GetServerMapDetailAsync(string serverGuid, string mapName, int days = 60)
    {
        // Get server info (including IsOnline status from database)
        var server = await dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Guid == serverGuid);

        if (server == null)
            return null;

        // Use the IsOnline flag from the database (maintained by background job)
        var isOnline = server.IsOnline;

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Query 1: Map Activity from ServerMapStats
        var mapActivitySql = @"
            SELECT
                COALESCE(SUM(TotalRounds), 0) as TotalRounds,
                COALESCE(SUM(TotalPlayTimeMinutes), 0) as TotalPlayTimeMinutes,
                ROUND(COALESCE(AVG(AvgConcurrentPlayers), 0), 1) as AvgConcurrentPlayers,
                COALESCE(MAX(PeakConcurrentPlayers), 0) as PeakConcurrentPlayers,
                COALESCE(SUM(Team1Victories), 0) as Team1Victories,
                COALESCE(SUM(Team2Victories), 0) as Team2Victories,
                MAX(Team1Label) as Team1Label,
                MAX(Team2Label) as Team2Label
            FROM ServerMapStats
            WHERE ServerGuid = @p0 AND MapName = @p1
              AND ((Year > @p2) OR (Year = @p2 AND Month >= @p3))";

        var mapActivityData = await dbContext.Database
            .SqlQueryRaw<ServerMapActivityQueryResult>(mapActivitySql, serverGuid, mapName, cutoffYear, cutoffMonth)
            .FirstOrDefaultAsync();

        // If no data found for this server/map combination
        if (mapActivityData == null || mapActivityData.TotalRounds == 0)
            return null;

        var mapActivity = new MapActivityStatsDto(
            TotalRounds: mapActivityData.TotalRounds,
            TotalPlayTimeMinutes: mapActivityData.TotalPlayTimeMinutes,
            AvgConcurrentPlayers: mapActivityData.AvgConcurrentPlayers,
            PeakConcurrentPlayers: mapActivityData.PeakConcurrentPlayers
        );

        // Calculate win stats
        var totalWins = mapActivityData.Team1Victories + mapActivityData.Team2Victories;
        var winStats = new WinStatsDto(
            Team1Label: mapActivityData.Team1Label ?? "Team 1",
            Team2Label: mapActivityData.Team2Label ?? "Team 2",
            Team1Victories: mapActivityData.Team1Victories,
            Team2Victories: mapActivityData.Team2Victories,
            Team1WinPercentage: totalWins > 0 ? Math.Round(100.0 * mapActivityData.Team1Victories / totalWins, 1) : 0,
            Team2WinPercentage: totalWins > 0 ? Math.Round(100.0 * mapActivityData.Team2Victories / totalWins, 1) : 0,
            TotalRounds: mapActivityData.TotalRounds
        );

        // Query 2: Player Leaderboards from PlayerMapStats
        var playerStatsSql = @"
            SELECT
                PlayerName,
                SUM(TotalScore) as TotalScore,
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                CASE WHEN SUM(TotalDeaths) > 0 
                     THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths), 2) 
                     ELSE CAST(SUM(TotalKills) AS REAL) END as KdRatio,
                CASE WHEN SUM(TotalPlayTimeMinutes) > 0 
                     THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalPlayTimeMinutes), 3) 
                     ELSE 0 END as KillsPerMinute,
                SUM(TotalRounds) as TotalRounds,
                SUM(TotalPlayTimeMinutes) as TotalPlayTimeMinutes
            FROM PlayerMapStats
            WHERE ServerGuid = @p0 AND MapName = @p1 
              AND ((Year > @p2) OR (Year = @p2 AND Month >= @p3))
            GROUP BY PlayerName
            HAVING SUM(TotalRounds) >= 3";

        var playerStats = await dbContext.Database
            .SqlQueryRaw<PlayerLeaderboardQueryResult>(playerStatsSql, serverGuid, mapName, cutoffYear, cutoffMonth)
            .ToListAsync();

        // Create leaderboard entries from the player stats
        var allEntries = playerStats.Select(p => new LeaderboardEntryDto(
            PlayerName: p.PlayerName,
            TotalScore: p.TotalScore,
            TotalKills: p.TotalKills,
            TotalWins: 0,
            TotalDeaths: p.TotalDeaths,
            KdRatio: p.KdRatio,
            KillsPerMinute: p.KillsPerMinute,
            TotalRounds: p.TotalRounds,
            PlayTimeMinutes: p.TotalPlayTimeMinutes
        )).ToList();

        var entryByPlayer = allEntries.ToDictionary(e => e.PlayerName, StringComparer.Ordinal);

        // Sort and get top 10 for each category
        var topByScore = allEntries
            .OrderByDescending(e => e.TotalScore)
            .Take(10)
            .ToList();

        var topByKills = allEntries
            .OrderByDescending(e => e.TotalKills)
            .Take(10)
            .ToList();

        // Top winners from placement achievements (1st place = tier gold) for this server/map/time range
        var topByWinsRaw = await dbContext.PlayerAchievements
            .AsNoTracking()
            .Where(a => a.ServerGuid == serverGuid
                && a.MapName == mapName
                && a.AchievementType == AchievementTypes.Placement
                && a.Tier == "gold"
                && a.AchievedAt >= Instant.FromDateTimeUtc(DateTime.SpecifyKind(fromDate, DateTimeKind.Utc)))
            .GroupBy(a => a.PlayerName)
            .Select(g => new { PlayerName = g.Key, Wins = g.Count() })
            .OrderByDescending(x => x.Wins)
            .ThenBy(x => x.PlayerName)
            .Take(10)
            .ToListAsync();

        var topByWins = topByWinsRaw
            .Select(w =>
            {
                if (entryByPlayer.TryGetValue(w.PlayerName, out var existing))
                {
                    return existing with { TotalWins = w.Wins };
                }

                return new LeaderboardEntryDto(
                    PlayerName: w.PlayerName,
                    TotalScore: 0,
                    TotalKills: 0,
                    TotalWins: w.Wins,
                    TotalDeaths: 0,
                    KdRatio: 0,
                    KillsPerMinute: 0,
                    TotalRounds: 0,
                    PlayTimeMinutes: 0
                );
            })
            .ToList();

        var topByKdRatio = allEntries
            .OrderByDescending(e => e.KdRatio)
            .Take(10)
            .ToList();

        var topByKillRate = allEntries
            .Where(e => e.PlayTimeMinutes >= 10) // Minimum 10 minutes playtime for kill rate
            .OrderByDescending(e => e.KillsPerMinute)
            .Take(10)
            .ToList();

        var dateRange = new DateRangeDto(
            Days: days,
            FromDate: fromDate,
            ToDate: toDate
        );

        // Get activity patterns for this specific server + map
        var activityPatterns = await dbContext.MapServerHourlyPatterns
            .Where(p => p.ServerGuid == serverGuid && p.MapName == mapName)
            .OrderBy(p => p.DayOfWeek)
            .ThenBy(p => p.HourOfDay)
            .Select(p => new ActivityPatternDto(
                p.DayOfWeek,
                p.HourOfDay,
                Math.Round(p.AvgPlayers, 1),
                Math.Round(p.AvgPlayers, 1)  // Use avg as median placeholder
            ))
            .ToListAsync();

        return new ServerMapDetailDto(
            ServerGuid: serverGuid,
            ServerName: server.Name,
            MapName: mapName,
            Game: server.Game,
            IsServerOnline: isOnline,
            MapActivity: mapActivity,
            WinStats: winStats,
            TopByScore: topByScore,
            TopByKills: topByKills,
            TopByWins: topByWins,
            TopByKdRatio: topByKdRatio,
            TopByKillRate: topByKillRate,
            ActivityPatterns: activityPatterns,
            DateRange: dateRange
        );
    }

    public async Task<PlayerSearchResponse> SearchPlayersAsync(string query, string game = "bf1942")
    {
        var normalizedGame = NormalizeGame(game);

        // Require minimum 3 characters for search
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            return new PlayerSearchResponse([], 0, query);

        // Get server GUIDs for the specified game
        var serverGuids = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => s.Guid)
            .ToListAsync();

        if (serverGuids.Count == 0)
            return new PlayerSearchResponse([], 0, query);

        // Build parameterized IN clause for server GUIDs
        var guidParams = string.Join(", ", serverGuids.Select((_, i) => $"@p{i + 1}"));

        // Search players with stats aggregated across all maps/servers in the game
        var searchSql = $@"
            SELECT
                PlayerName,
                SUM(TotalScore) as TotalScore,
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                CASE WHEN SUM(TotalDeaths) > 0
                     THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths), 2)
                     ELSE CAST(SUM(TotalKills) AS REAL) END as KdRatio,
                SUM(TotalRounds) as TotalRounds,
                COUNT(DISTINCT MapName) as UniqueMaps,
                COUNT(DISTINCT ServerGuid) as UniqueServers
            FROM PlayerMapStats
            WHERE PlayerName LIKE @p0 || '%'
              AND ServerGuid IN ({guidParams})
            GROUP BY PlayerName
            ORDER BY SUM(TotalScore) DESC
            LIMIT 50";

        var sqlParams = new List<object> { query };
        sqlParams.AddRange(serverGuids.Cast<object>());

        var players = await dbContext.Database
            .SqlQueryRaw<PlayerSearchQueryResult>(searchSql, sqlParams.ToArray())
            .ToListAsync();

        var results = players.Select(p => new PlayerSearchResultDto(
            PlayerName: p.PlayerName,
            TotalScore: p.TotalScore,
            TotalKills: p.TotalKills,
            TotalDeaths: p.TotalDeaths,
            KdRatio: p.KdRatio,
            TotalRounds: p.TotalRounds,
            UniqueMaps: p.UniqueMaps,
            UniqueServers: p.UniqueServers
        )).ToList();

        return new PlayerSearchResponse(results, results.Count, query);
    }

    public async Task<PlayerMapRankingsResponse?> GetPlayerMapRankingsAsync(string playerName, string game = "bf1942", int days = 60, string? serverGuid = null)
    {
        var normalizedGame = NormalizeGame(game);

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Get server GUIDs and names for the specified game
        var serversQuery = dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame);

        // Filter to specific server if provided
        if (!string.IsNullOrEmpty(serverGuid))
        {
            serversQuery = serversQuery.Where(s => s.Guid == serverGuid);
        }

        var servers = await serversQuery
            .Select(s => new { s.Guid, s.Name })
            .ToListAsync();

        if (servers.Count == 0)
            return null;

        var serverGuids = servers.Select(s => s.Guid).ToList();
        var serverNameLookup = servers.ToDictionary(s => s.Guid, s => s.Name);

        // Build parameterized IN clause for server GUIDs
        var guidParams = string.Join(", ", serverGuids.Select((_, i) => $"@p{i + 3}"));

        // Query player stats grouped by map and server
        var playerStatsSql = $@"
            SELECT
                MapName,
                ServerGuid,
                SUM(TotalScore) as TotalScore,
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                CASE WHEN SUM(TotalDeaths) > 0
                     THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths), 2)
                     ELSE CAST(SUM(TotalKills) AS REAL) END as KdRatio,
                SUM(TotalRounds) as TotalRounds
            FROM PlayerMapStats
            WHERE PlayerName = @p0
              AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
              AND ServerGuid IN ({guidParams})
            GROUP BY MapName, ServerGuid
            ORDER BY MapName, SUM(TotalScore) DESC";

        var sqlParams = new List<object> { playerName, cutoffYear, cutoffMonth };
        sqlParams.AddRange(serverGuids.Cast<object>());

        var playerStats = await dbContext.Database
            .SqlQueryRaw<PlayerMapServerStatsQueryResult>(playerStatsSql, sqlParams.ToArray())
            .ToListAsync();

        if (playerStats.Count == 0)
            return null;

        // Get rankings for each map/server combination
        // We need to calculate the player's rank on each server for each map
        // Build separate guidParams for this query (starts at @p2 since we have year, month first)
        var rankingGuidParams = string.Join(", ", serverGuids.Select((_, i) => $"@p{i + 2}"));
        var playerNameParamIndex = 2 + serverGuids.Count;

        var rankingSql = $@"
            WITH PlayerRankings AS (
                SELECT
                    MapName,
                    ServerGuid,
                    PlayerName,
                    SUM(TotalScore) as TotalScore,
                    ROW_NUMBER() OVER (PARTITION BY MapName, ServerGuid ORDER BY SUM(TotalScore) DESC) as Rank
                FROM PlayerMapStats
                WHERE ((Year > @p0) OR (Year = @p0 AND Month >= @p1))
                  AND ServerGuid IN ({rankingGuidParams})
                GROUP BY MapName, ServerGuid, PlayerName
            )
            SELECT MapName, ServerGuid, Rank, TotalScore
            FROM PlayerRankings
            WHERE PlayerName = @p{playerNameParamIndex}";

        var rankingParams = new List<object> { cutoffYear, cutoffMonth };
        rankingParams.AddRange(serverGuids.Cast<object>());
        rankingParams.Add(playerName);

        var rankings = await dbContext.Database
            .SqlQueryRaw<PlayerRankingQueryResult>(rankingSql, rankingParams.ToArray())
            .ToListAsync();

        var rankingLookup = rankings.ToDictionary(
            r => (r.MapName, r.ServerGuid),
            r => (int)r.Rank
        );

        // Build map groups with server stats
        var mapGroups = playerStats
            .GroupBy(ps => ps.MapName)
            .Select(mapGroup =>
            {
                var serverStats = mapGroup.Select(ps =>
                {
                    rankingLookup.TryGetValue((ps.MapName, ps.ServerGuid), out var rank);
                    return new PlayerServerStatsDto(
                        ServerGuid: ps.ServerGuid,
                        ServerName: serverNameLookup.GetValueOrDefault(ps.ServerGuid, ps.ServerGuid),
                        TotalScore: ps.TotalScore,
                        TotalKills: ps.TotalKills,
                        TotalDeaths: ps.TotalDeaths,
                        KdRatio: ps.KdRatio,
                        TotalRounds: ps.TotalRounds,
                        Rank: rank
                    );
                })
                .OrderBy(ss => ss.Rank)
                .ToList();

                var bestServer = serverStats.MinBy(ss => ss.Rank);

                return new PlayerMapGroupDto(
                    MapName: mapGroup.Key,
                    AggregatedScore: mapGroup.Sum(ps => ps.TotalScore),
                    ServerStats: serverStats,
                    BestRank: bestServer?.Rank,
                    BestRankServer: bestServer?.ServerName
                );
            })
            .OrderByDescending(mg => mg.AggregatedScore)
            .ToList();

        // Build #1 rankings list
        var numberOneRankings = mapGroups
            .SelectMany(mg => mg.ServerStats
                .Where(ss => ss.Rank == 1)
                .Select(ss => new NumberOneRankingDto(
                    MapName: mg.MapName,
                    ServerName: ss.ServerName,
                    ServerGuid: ss.ServerGuid,
                    TotalScore: ss.TotalScore
                )))
            .OrderByDescending(r => r.TotalScore)
            .ToList();

        // Calculate overall stats
        var overallStats = new PlayerOverallStatsDto(
            TotalScore: playerStats.Sum(ps => ps.TotalScore),
            TotalKills: playerStats.Sum(ps => ps.TotalKills),
            TotalDeaths: playerStats.Sum(ps => ps.TotalDeaths),
            KdRatio: playerStats.Sum(ps => ps.TotalDeaths) > 0
                ? Math.Round((double)playerStats.Sum(ps => ps.TotalKills) / playerStats.Sum(ps => ps.TotalDeaths), 2)
                : playerStats.Sum(ps => ps.TotalKills),
            TotalRounds: playerStats.Sum(ps => ps.TotalRounds),
            UniqueServers: playerStats.Select(ps => ps.ServerGuid).Distinct().Count(),
            UniqueMaps: playerStats.Select(ps => ps.MapName).Distinct().Count()
        );

        var dateRange = new DateRangeDto(
            Days: days,
            FromDate: fromDate,
            ToDate: toDate
        );

        return new PlayerMapRankingsResponse(
            PlayerName: playerName,
            Game: normalizedGame,
            OverallStats: overallStats,
            MapGroups: mapGroups,
            NumberOneRankings: numberOneRankings,
            DateRange: dateRange
        );
    }

    public async Task<MapPlayerRankingsResponse?> GetMapPlayerRankingsAsync(
        string mapName,
        string game = "bf1942",
        int page = 1,
        int pageSize = 10,
        string? searchQuery = null,
        string? serverGuid = null,
        int days = 60,
        string sortBy = "score")
    {
        var normalizedGame = NormalizeGame(game);

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Get server GUIDs for the specified game
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => s.Guid)
            .ToListAsync();

        if (servers.Count == 0)
            return null;

        // Filter by specific server if provided
        var targetServerGuids = !string.IsNullOrWhiteSpace(serverGuid)
            ? servers.Where(g => g == serverGuid).ToList()
            : servers;

        // Always include empty string for global stats unless filtering by specific server
        if (string.IsNullOrWhiteSpace(serverGuid))
        {
            targetServerGuids = targetServerGuids.Concat(new[] { "" }).ToList();
        }

        if (targetServerGuids.Count == 0)
            return null;

        // Build the base query parameters
        var sqlParams = new List<object> { mapName, cutoffYear, cutoffMonth };
        var paramOffset = 3;

        // Build server GUIDs IN clause
        var guidParams = string.Join(", ", targetServerGuids.Select((_, i) => $"@p{i + paramOffset}"));
        sqlParams.AddRange(targetServerGuids.Cast<object>());
        paramOffset += targetServerGuids.Count;

        // Build optional player name filter
        var playerFilter = "";
        if (!string.IsNullOrWhiteSpace(searchQuery) && searchQuery.Length >= 2)
        {
            playerFilter = $" AND PlayerName LIKE @p{paramOffset} || '%'";
            sqlParams.Add(searchQuery);
            paramOffset++;
        }

        // Count total matching players (for pagination)
        // Must use same HAVING filter as data query to get accurate count
        var countSql = $@"
            SELECT COUNT(*) as Value
            FROM (
                SELECT PlayerName
                FROM PlayerMapStats
                WHERE MapName = @p0
                  AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
                  AND ServerGuid IN ({guidParams})
                  {playerFilter}
                GROUP BY PlayerName
                HAVING SUM(TotalRounds) >= 3
            )";

        var totalCount = await dbContext.Database
            .SqlQueryRaw<int>(countSql, sqlParams.ToArray())
            .FirstOrDefaultAsync();

        // Calculate offset for pagination
        var offset = (page - 1) * pageSize;

        // Determine sort column and ORDER BY clause
        var (sortColumn, orderByClause) = sortBy.ToLowerInvariant() switch
        {
            "kills" => ("TotalKills", "TotalKills DESC"),
            "kdratio" => ("KdRatio", "CAST(CalcKdRatio AS REAL) DESC"),
            "killrate" => ("KillsPerMinute", "CAST(CalcKillsPerMinute AS REAL) DESC"),
            "wins" => ("TotalWins", "COALESCE(pw.Wins, 0) DESC"),
            _ => ("TotalScore", "TotalScore DESC") // default to score
        };

        // Query player rankings with pagination
        // Use CTE for wins calculation from PlayerAchievements
        var rankingsSql = $@"
            WITH PlayerWins AS (
                SELECT PlayerName, COUNT(*) as Wins
                FROM PlayerAchievements
                WHERE MapName = @p0
                  AND AchievementType = '{AchievementTypes.Placement}'
                  AND Tier = 'gold'
                  AND ((CAST(strftime('%Y', AchievedAt) AS INTEGER) > @p1)
                       OR (CAST(strftime('%Y', AchievedAt) AS INTEGER) = @p1
                           AND CAST(strftime('%m', AchievedAt) AS INTEGER) >= @p2))
                  AND ServerGuid IN ({guidParams})
                GROUP BY PlayerName
            )
            SELECT
                ROW_NUMBER() OVER (ORDER BY {orderByClause}) as Rank,
                t.PlayerName,
                TotalScore,
                TotalKills,
                TotalDeaths,
                CalcKdRatio as KdRatio,
                CalcKillsPerMinute as KillsPerMinute,
                TotalRounds,
                TotalPlayTimeMinutes as PlayTimeMinutes,
                UniqueServers,
                COALESCE(pw.Wins, 0) as TotalWins
            FROM (
                SELECT
                    PlayerName,
                    SUM(TotalScore) as TotalScore,
                    SUM(TotalKills) as TotalKills,
                    SUM(TotalDeaths) as TotalDeaths,
                    CASE WHEN SUM(TotalDeaths) > 0
                         THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths), 2)
                         ELSE CAST(SUM(TotalKills) AS REAL) END as CalcKdRatio,
                    CASE WHEN SUM(TotalPlayTimeMinutes) > 0
                         THEN ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalPlayTimeMinutes), 3)
                         ELSE 0 END as CalcKillsPerMinute,
                    SUM(TotalRounds) as TotalRounds,
                    SUM(TotalPlayTimeMinutes) as TotalPlayTimeMinutes,
                    COUNT(DISTINCT ServerGuid) as UniqueServers
                FROM PlayerMapStats
                WHERE MapName = @p0
                  AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
                  AND ServerGuid IN ({guidParams})
                  {playerFilter}
                GROUP BY PlayerName
                HAVING SUM(TotalRounds) >= 3
            ) t
            LEFT JOIN PlayerWins pw ON t.PlayerName = pw.PlayerName
            ORDER BY {orderByClause}
            LIMIT @p{paramOffset} OFFSET @p{paramOffset + 1}";

        sqlParams.Add(pageSize);
        sqlParams.Add(offset);

        var rankings = await dbContext.Database
            .SqlQueryRaw<MapPlayerRankingQueryResult>(rankingsSql, sqlParams.ToArray())
            .ToListAsync();

        var rankingDtos = rankings.Select(r => new MapPlayerRankingDto(
            Rank: (int)r.Rank + offset, // Adjust rank for pagination
            PlayerName: r.PlayerName,
            TotalScore: r.TotalScore,
            TotalKills: r.TotalKills,
            TotalDeaths: r.TotalDeaths,
            KdRatio: r.KdRatio,
            KillsPerMinute: r.KillsPerMinute,
            TotalRounds: r.TotalRounds,
            PlayTimeMinutes: r.PlayTimeMinutes,
            UniqueServers: r.UniqueServers,
            TotalWins: r.TotalWins
        )).ToList();

        var dateRange = new DateRangeDto(
            Days: days,
            FromDate: fromDate,
            ToDate: toDate
        );

        return new MapPlayerRankingsResponse(
            MapName: mapName,
            Game: normalizedGame,
            Rankings: rankingDtos,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            DateRange: dateRange
        );
    }

    public async Task<MapActivityPatternsResponse?> GetMapActivityPatternsAsync(string mapName, string game = "bf1942")
    {
        var normalizedGame = NormalizeGame(game);

        logger.LogDebug("Getting map activity patterns for {MapName} with game filter {Game}", mapName, normalizedGame);

        // Query the pre-computed MapServerHourlyPatterns table and aggregate across all servers
        var patterns = await dbContext.MapServerHourlyPatterns
            .Where(p => p.MapName == mapName && p.Game == normalizedGame)
            .GroupBy(p => new { p.DayOfWeek, p.HourOfDay })
            .Select(g => new
            {
                g.Key.DayOfWeek,
                g.Key.HourOfDay,
                AvgPlayers = g.Sum(p => p.AvgPlayers),
                TimesPlayed = g.Sum(p => p.TimesPlayed)
            })
            .OrderBy(p => p.DayOfWeek).ThenBy(p => p.HourOfDay)
            .ToListAsync();

        // Apply rounding on the client side since Math.Round cannot be translated to SQL
        var roundedPatterns = patterns
            .Select(p => new MapActivityPatternDto(
                p.DayOfWeek,
                p.HourOfDay,
                Math.Round(p.AvgPlayers, 2),
                p.TimesPlayed
            ))
            .ToList();

        if (roundedPatterns.Count == 0)
        {
            logger.LogDebug("No activity patterns found for map {MapName} in game {Game}", mapName, normalizedGame);
            return null;
        }

        // Calculate total data points from the patterns (sum of distinct days with data)
        var totalDataPoints = await dbContext.MapServerHourlyPatterns
            .Where(p => p.MapName == mapName && p.Game == normalizedGame)
            .SumAsync(p => p.DataPoints);

        return new MapActivityPatternsResponse(
            MapName: mapName,
            Game: normalizedGame,
            ActivityPatterns: roundedPatterns,
            TotalDataPoints: totalDataPoints
        );
    }

    #region Query Result DTOs

    private class ServerStatsQueryResult
    {
        public string ServerGuid { get; set; } = "";
        public int TotalMaps { get; set; }
        public int TotalRounds { get; set; }
    }

    private class MapRotationQueryResult
    {
        public string MapName { get; set; } = "";
        public int TotalRounds { get; set; }
        public int TotalPlayTimeMinutes { get; set; }
        public double PlayTimePercentage { get; set; }
        public double AvgConcurrentPlayers { get; set; }
        public int Team1Victories { get; set; }
        public int Team2Victories { get; set; }
        public double Team1WinPercentage { get; set; }
        public double Team2WinPercentage { get; set; }
        public string? Team1Label { get; set; }
        public string? Team2Label { get; set; }
    }

    private class TopPlayerQueryResult
    {
        public string MapName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double KdRatio { get; set; }
        public int Rank { get; set; }
    }

    private class MapTopWinnerQueryResult
    {
        public string MapName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public int Wins { get; set; }
    }

    private class MapStatsQueryResult
    {
        public string MapName { get; set; } = "";
        public int ServersPlayingCount { get; set; }
        public int TotalRoundsLast30Days { get; set; }
        public double AvgPlayersWhenPlayed { get; set; }
    }

    private class ServerOnMapQueryResult
    {
        public string ServerGuid { get; set; } = "";
        public int TotalRounds { get; set; }
        public int Team1Victories { get; set; }
        public int Team2Victories { get; set; }
        public double Team1WinPercentage { get; set; }
        public double Team2WinPercentage { get; set; }
        public string? Team1Label { get; set; }
        public string? Team2Label { get; set; }
    }


    private class ServerMapActivityQueryResult
    {
        public int TotalRounds { get; set; }
        public int TotalPlayTimeMinutes { get; set; }
        public double AvgConcurrentPlayers { get; set; }
        public int PeakConcurrentPlayers { get; set; }
        public int Team1Victories { get; set; }
        public int Team2Victories { get; set; }
        public string? Team1Label { get; set; }
        public string? Team2Label { get; set; }
    }

    private class PlayerLeaderboardQueryResult
    {
        public string PlayerName { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double KdRatio { get; set; }
        public double KillsPerMinute { get; set; }
        public int TotalRounds { get; set; }
        public double TotalPlayTimeMinutes { get; set; }
    }

    private class PlayerSearchQueryResult
    {
        public string PlayerName { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double KdRatio { get; set; }
        public int TotalRounds { get; set; }
        public int UniqueMaps { get; set; }
        public int UniqueServers { get; set; }
    }

    private class PlayerMapServerStatsQueryResult
    {
        public string MapName { get; set; } = "";
        public string ServerGuid { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double KdRatio { get; set; }
        public int TotalRounds { get; set; }
    }

    private class PlayerRankingQueryResult
    {
        public string PlayerName { get; set; } = "";
        public string MapName { get; set; } = "";
        public string ServerGuid { get; set; } = "";
        public int Score { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Rounds { get; set; }
        public double PlayTime { get; set; }
        public long Rank { get; set; }
        public long TotalPlayers { get; set; }
        public int TotalScore { get; set; } // For backward compatibility
    }

    private class MapPlayerRankingQueryResult
    {
        public long Rank { get; set; }
        public string PlayerName { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double KdRatio { get; set; }
        public double KillsPerMinute { get; set; }
        public int TotalRounds { get; set; }
        public double PlayTimeMinutes { get; set; }
        public int UniqueServers { get; set; }
        public int TotalWins { get; set; }
    }

    /// <inheritdoc/>
    public async Task<ServerEngagementStatsDto> GetServerEngagementStatsAsync(string serverGuid)
    {
        logger.LogInformation("Getting randomized engagement stats for server: {ServerGuid}", serverGuid);

        var stats = new List<ServerEngagementStat>();

        // Randomize time period: last 1 month, last 2 months, or current month
        var random = new Random();
        var timePeriod = random.Next(3); // 0, 1, or 2

        var now = DateTime.UtcNow;
        int monthsBack;
        string timeLabel;

        switch (timePeriod)
        {
            case 0: // Last month
                monthsBack = 1;
                timeLabel = "last month";
                break;
            case 1: // Last 2 months
                monthsBack = 2;
                timeLabel = "last 2 months";
                break;
            case 2: // Current month
                monthsBack = 0;
                timeLabel = "this month";
                break;
            default:
                monthsBack = 1;
                timeLabel = "last month";
                break;
        }

        // Calculate year/months to include
        var cutoffDate = now.AddMonths(-monthsBack);
        var cutoffYear = cutoffDate.Year;
        var cutoffMonth = cutoffDate.Month;

        // Stat 1: Total rounds in selected period
        var totalRounds = await dbContext.ServerMapStats
            .Where(sms => sms.ServerGuid == serverGuid &&
                         (sms.Year > cutoffYear || (sms.Year == cutoffYear && sms.Month >= cutoffMonth)))
            .SumAsync(sms => sms.TotalRounds);

        var roundMessages = new[]
        {
            $"{totalRounds:N0} battles fought {timeLabel}. Who's leading the charge?",
            $"{totalRounds:N0} rounds of carnage {timeLabel}. See who dominated.",
            $"{totalRounds:N0} matches hosted {timeLabel}. Find the top fraggers.",
            $"{totalRounds:N0} rounds played. Discover the legends of this server."
        };

        stats.Add(new ServerEngagementStat
        {
            Value = totalRounds.ToString("N0"),
            Label = "rounds played",
            Context = $"Total matches in {timeLabel}",
            Message = roundMessages[random.Next(roundMessages.Length)]
        });

        // Stat 2: Unique maps played in selected period
        var uniqueMaps = await dbContext.ServerMapStats
            .Where(sms => sms.ServerGuid == serverGuid &&
                         (sms.Year > cutoffYear || (sms.Year == cutoffYear && sms.Month >= cutoffMonth)))
            .Select(sms => sms.MapName)
            .Distinct()
            .CountAsync();

        var mapMessages = new[]
        {
            $"{uniqueMaps} maps in rotation. Discover the kings of each battlefield.",
            $"{uniqueMaps} battlefields await. Who dominates each one?",
            $"{uniqueMaps} maps, countless rivalries. Find your name on the leaderboards.",
            $"This server runs {uniqueMaps} maps. See who owns them."
        };

        stats.Add(new ServerEngagementStat
        {
            Value = uniqueMaps.ToString("N0"),
            Label = "maps in rotation",
            Context = $"Active in {timeLabel}",
            Message = mapMessages[random.Next(mapMessages.Length)]
        });

        // Stat 3: Peak concurrent players from activity patterns
        var peakPlayers = await dbContext.MapServerHourlyPatterns
            .Where(mshp => mshp.ServerGuid == serverGuid)
            .MaxAsync(mshp => (double?)mshp.AvgPlayers) ?? 0;

        var peakPlayerMessages = new[]
        {
            $"Peak of {Math.Round(peakPlayers)} soldiers at once. When's the best time to strike?",
            $"Up to {Math.Round(peakPlayers)} players in battle. See when this server gets wild.",
            $"{Math.Round(peakPlayers)} concurrent warriors at peak. Find the hot hours.",
            $"This server hits {Math.Round(peakPlayers)} players. Check the activity patterns."
        };

        stats.Add(new ServerEngagementStat
        {
            Value = Math.Round(peakPlayers).ToString("N0"),
            Label = "peak concurrent players",
            Context = "Highest count recorded",
            Message = peakPlayerMessages[random.Next(peakPlayerMessages.Length)]
        });

        // Stat 4: Total unique players in selected period (bonus stat)
        var totalPlayers = await dbContext.PlayerMapStats
            .Where(pms => pms.ServerGuid == serverGuid &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .Select(pms => pms.PlayerName)
            .Distinct()
            .CountAsync();

        var playerMessages = new[]
        {
            $"{totalPlayers:N0} players have fought here {timeLabel}. Find your rivals.",
            $"{totalPlayers:N0} warriors battled {timeLabel}. See who came out on top.",
            $"{totalPlayers:N0} soldiers, one leaderboard. Where do you rank?",
            $"{totalPlayers:N0} unique combatants. Discover the elite of this server."
        };

        stats.Add(new ServerEngagementStat
        {
            Value = totalPlayers.ToString("N0"),
            Label = "unique players",
            Context = $"Active in {timeLabel}",
            Message = playerMessages[random.Next(playerMessages.Length)]
        });

        // Shuffle the stats for randomization and take 3
        var randomizedStats = stats.OrderBy(_ => random.Next()).Take(3).ToArray();

        return new ServerEngagementStatsDto { Stats = randomizedStats };
    }

    /// <inheritdoc/>
    public async Task<PlayerEngagementStatsDto> GetPlayerEngagementStatsAsync(string playerName, string game = "bf1942")
    {
        logger.LogInformation("Getting randomized engagement stats for player: {PlayerName}", playerName);

        var stats = new List<PlayerEngagementStat>();

        // Randomize time period: last 1 month, last 2 months, or current month
        var random = new Random();
        var timePeriod = random.Next(3); // 0, 1, or 2

        var now = DateTime.UtcNow;
        int monthsBack;
        string timeLabel;

        switch (timePeriod)
        {
            case 0: // Last month
                monthsBack = 1;
                timeLabel = "last month";
                break;
            case 1: // Last 2 months
                monthsBack = 2;
                timeLabel = "last 2 months";
                break;
            case 2: // Current month
                monthsBack = 0;
                timeLabel = "this month";
                break;
            default:
                monthsBack = 1;
                timeLabel = "last month";
                break;
        }

        // Calculate year/months to include
        var cutoffDate = now.AddMonths(-monthsBack);
        var cutoffYear = cutoffDate.Year;
        var cutoffMonth = cutoffDate.Month;

        // Stat 1: Total kills and rounds in selected period
        var periodStats = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .GroupBy(_ => 1)
            .Select(g => new { TotalKills = g.Sum(pms => pms.TotalKills), TotalRounds = g.Sum(pms => pms.TotalRounds) })
            .FirstOrDefaultAsync();

        if (periodStats != null && periodStats.TotalKills > 0)
        {
            var killMessages = new[]
            {
                $"{periodStats.TotalKills:N0} kills {timeLabel}. See how you stack up against the competition.",
                $"{periodStats.TotalKills:N0} confirmed kills. Are you climbing the ranks?",
                $"{periodStats.TotalKills:N0} enemies down {timeLabel}. Where does that put you?",
                $"Racked up {periodStats.TotalKills:N0} kills. Let's see who you're really up against."
            };

            stats.Add(new PlayerEngagementStat
            {
                Value = periodStats.TotalKills.ToString("N0"),
                Label = "kills earned",
                Context = $"{periodStats.TotalRounds:N0} rounds in {timeLabel}",
                Message = killMessages[random.Next(killMessages.Length)]
            });
        }

        // Stat 2: Number of unique servers played on in period
        var uniqueServers = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .Select(pms => pms.ServerGuid)
            .Distinct()
            .CountAsync();

        if (uniqueServers > 0)
        {
            var serverMessages = new[]
            {
                $"Active on {uniqueServers} servers {timeLabel}. See your rank on each one.",
                $"Fought across {uniqueServers} battlefields. Check your standings everywhere.",
                $"{uniqueServers} servers, {uniqueServers} leaderboards. Where do you dominate?",
                $"You've hit {uniqueServers} servers {timeLabel}. Discover your rankings."
            };

            stats.Add(new PlayerEngagementStat
            {
                Value = uniqueServers.ToString("N0"),
                Label = "servers active on",
                Context = $"In {timeLabel}",
                Message = serverMessages[random.Next(serverMessages.Length)]
            });
        }

        // Stat 3: Number of unique maps played in period
        var uniqueMaps = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .Select(pms => pms.MapName)
            .Distinct()
            .CountAsync();

        if (uniqueMaps > 0)
        {
            var mapMessages = new[]
            {
                $"{uniqueMaps} maps conquered {timeLabel}. Who else is dominating them?",
                $"Played {uniqueMaps} different maps. Ready to claim map leaderboards?",
                $"{uniqueMaps} battlefields, endless rivalries. Find your name in the rankings.",
                $"You've fought on {uniqueMaps} maps. See where you rank on each."
            };

            stats.Add(new PlayerEngagementStat
            {
                Value = uniqueMaps.ToString("N0"),
                Label = "maps played",
                Context = $"In {timeLabel}",
                Message = mapMessages[random.Next(mapMessages.Length)]
            });
        }

        // Stat 4: Best K/D ratio from period data
        var bestKdStats = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)) &&
                         pms.TotalDeaths > 0)
            .Select(pms => new { KdRatio = (double)pms.TotalKills / pms.TotalDeaths, pms.TotalKills, pms.TotalDeaths })
            .OrderByDescending(x => x.KdRatio)
            .FirstOrDefaultAsync();

        if (bestKdStats != null)
        {
            var kdMessages = new[]
            {
                $"A {bestKdStats.KdRatio:N2} K/D ratio. Think that's top tier? Let's find out.",
                $"{bestKdStats.KdRatio:N2} K/D. See how you measure up against the best.",
                $"Sitting at {bestKdStats.KdRatio:N2} K/D. Where does that rank you?",
                $"Your {bestKdStats.KdRatio:N2} K/D ratio - impressive or just average? Check the rankings."
            };

            stats.Add(new PlayerEngagementStat
            {
                Value = bestKdStats.KdRatio.ToString("N2"),
                Label = "best K/D ratio",
                Context = $"{bestKdStats.TotalKills}K/{bestKdStats.TotalDeaths}D in {timeLabel}",
                Message = kdMessages[random.Next(kdMessages.Length)]
            });
        }

        // Stat 5: Most active map in period
        var favoriteMap = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .GroupBy(pms => pms.MapName)
            .Select(g => new { MapName = g.Key, TotalRounds = g.Sum(pms => pms.TotalRounds) })
            .OrderByDescending(x => x.TotalRounds)
            .FirstOrDefaultAsync();

        if (favoriteMap != null && favoriteMap.TotalRounds > 0)
        {
            var mapName = favoriteMap.MapName.Replace("_", " ");
            var favoriteMapMessages = new[]
            {
                $"{mapName} is your playground. Ready to claim the throne?",
                $"You've been grinding {mapName}. See who else rules that map.",
                $"{mapName} - {favoriteMap.TotalRounds} rounds {timeLabel}. Are you the top dog?",
                $"Most played: {mapName}. Time to check your ranking there."
            };

            stats.Add(new PlayerEngagementStat
            {
                Value = favoriteMap.MapName,
                Label = "most active map",
                Context = $"{favoriteMap.TotalRounds:N0} rounds in {timeLabel}",
                Message = favoriteMapMessages[random.Next(favoriteMapMessages.Length)]
            });
        }

        // Stat 6: All-time favorite map (if period data is sparse)
        if (stats.Count < 3)
        {
            var allTimeFavorite = await dbContext.PlayerMapStats
                .Where(pms => pms.PlayerName == playerName)
                .GroupBy(pms => pms.MapName)
                .Select(g => new { MapName = g.Key, TotalRounds = g.Sum(pms => pms.TotalRounds) })
                .OrderByDescending(x => x.TotalRounds)
                .FirstOrDefaultAsync();

            if (allTimeFavorite != null && allTimeFavorite.TotalRounds > 0 && !stats.Any(s => s.Value == allTimeFavorite.MapName))
            {
                var mapName = allTimeFavorite.MapName.Replace("_", " ");
                var allTimeMessages = new[]
                {
                    $"{mapName} - your all-time favorite. Own the leaderboard there?",
                    $"All-time most played: {mapName}. Who else dominates it?",
                    $"You've clocked {allTimeFavorite.TotalRounds:N0} rounds on {mapName}. Check your legacy."
                };

                stats.Add(new PlayerEngagementStat
                {
                    Value = allTimeFavorite.MapName,
                    Label = "all-time favorite",
                    Context = $"{allTimeFavorite.TotalRounds:N0} total rounds",
                    Message = allTimeMessages[random.Next(allTimeMessages.Length)]
                });
            }
        }

        // Shuffle the stats for randomization and take 3 (or fewer if not enough data)
        var availableStats = stats.Where(s => !string.IsNullOrEmpty(s.Value) && s.Value != "0").ToList();
        var randomizedStats = availableStats.OrderBy(_ => random.Next()).Take(Math.Min(3, availableStats.Count)).ToArray();

        // If we don't have enough stats, add some defaults
        if (randomizedStats.Length < 3)
        {
            var defaultMessages = new[]
            {
                "Start playing to unlock your stats and see where you rank!",
                "Get in the fight! Your stats are waiting to be built.",
                "Jump into battle and start climbing the leaderboards."
            };

            var defaultStats = new[]
            {
                new PlayerEngagementStat
                {
                    Value = "0",
                    Label = "recent activity",
                    Context = "Keep playing!",
                    Message = defaultMessages[random.Next(defaultMessages.Length)]
                }
            };

            var combinedStats = randomizedStats.Concat(defaultStats).Take(3).ToArray();
            randomizedStats = combinedStats;
        }

        return new PlayerEngagementStatsDto { Stats = randomizedStats };
    }

    /// <inheritdoc/>
    public async Task<PlayerSlicedStatsResponse> GetPlayerSlicedStatsAsync(
        string playerName,
        SliceDimensionType sliceType,
        string game = "bf1942",
        int page = 1,
        int pageSize = 20,
        int days = 60)
    {
        var normalizedGame = NormalizeGame(game);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Get server GUIDs for the specified game
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => new { s.Guid, s.Name })
            .ToListAsync();

        var serverGuids = servers.Select(s => s.Guid).ToList();
        var serverNameLookup = servers.ToDictionary(s => s.Guid, s => s.Name);

        // Generate results based on slice type
        // If there are no servers or no data, we'll return an empty list but preserve player context
        var results = servers.Count == 0 ? new List<PlayerSliceResultDto>() : sliceType switch
        {
            SliceDimensionType.WinsByMap => await GetWinsByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, false, null, normalizedGame),
            SliceDimensionType.WinsByMapAndServer => await GetWinsByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, true, serverNameLookup, normalizedGame),
            SliceDimensionType.ScoreByMap => await GetScoreByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, false, null, normalizedGame),
            SliceDimensionType.ScoreByMapAndServer => await GetScoreByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, true, serverNameLookup, normalizedGame),
            SliceDimensionType.KillsByMap => await GetKillsByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, false, null, normalizedGame),
            SliceDimensionType.KillsByMapAndServer => await GetKillsByMapAsync(playerName, serverGuids, cutoffYear, cutoffMonth, page, pageSize, true, serverNameLookup, normalizedGame),
            _ => new List<PlayerSliceResultDto>()
        };

        // Calculate pagination info (even for empty results to preserve context)
        var totalItems = results.Count;
        var totalPages = totalItems > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 0;

        var dateRange = new DateRangeDto(days, fromDate, toDate);
        var pagination = new PaginationDto(
            Page: page,
            PageSize: pageSize,
            TotalItems: totalItems,
            TotalPages: totalPages,
            HasNext: page < totalPages,
            HasPrevious: page > 1
        );

        return new PlayerSlicedStatsResponse(
            PlayerName: playerName,
            Game: normalizedGame,
            SliceDimension: GetSliceDimensionName(sliceType),
            SliceType: sliceType,
            Results: results,
            DateRange: dateRange,
            Pagination: pagination
        );
    }

    /// <inheritdoc/>
    public async Task<List<SliceDimensionOption>> GetAvailableSliceDimensionsAsync()
    {
        return await Task.FromResult(new List<SliceDimensionOption>
        {
            new(SliceDimensionType.WinsByMap, "Wins by Map", "Player wins (1st place finishes) aggregated across all servers per map"),
            new(SliceDimensionType.WinsByMapAndServer, "Wins by Map + Server", "Player wins (1st place finishes) per map per server combination"),
            new(SliceDimensionType.ScoreByMap, "Score by Map", "Total player score aggregated per map"),
            new(SliceDimensionType.ScoreByMapAndServer, "Score by Map + Server", "Player score per map per server"),
            new(SliceDimensionType.KillsByMap, "Kills by Map", "Total player kills aggregated per map"),
            new(SliceDimensionType.KillsByMapAndServer, "Kills by Map + Server", "Player kills per map per server")
        });
    }

    // Helper methods for different slice types
    private async Task<List<PlayerSliceResultDto>> GetWinsByMapAsync(
        string playerName,
        List<string> serverGuids,
        int cutoffYear,
        int cutoffMonth,
        int page,
        int pageSize,
        bool includeServer,
        Dictionary<string, string>? serverNameLookup = null,
        string game = "bf1942")
    {
        var groupBy = includeServer ? "pa.MapName, pa.ServerGuid" : "pa.MapName";
        var selectServerField = includeServer ? "pa.ServerGuid" : "NULL as ServerGuid";

        // Wins are modeled as placement "gold" achievements.
        // We join to PlayerMapStats (same grouping) to provide rounds for win rate context.
        var sql = $@"
            WITH PlayerWins AS (
                SELECT
                    pa.MapName,
                    {selectServerField},
                    COUNT(*) as Wins
                FROM PlayerAchievements pa
                WHERE pa.PlayerName = @p0
                  AND pa.AchievementType = @p1
                  AND pa.Tier = 'gold'
                  AND ((CAST(strftime('%Y', pa.AchievedAt) AS INTEGER) > @p2)
                       OR (CAST(strftime('%Y', pa.AchievedAt) AS INTEGER) = @p2
                           AND CAST(strftime('%m', pa.AchievedAt) AS INTEGER) >= @p3))
                  AND pa.ServerGuid IN (SELECT Guid FROM Servers WHERE Game = @p4)
                GROUP BY {groupBy}
            ),
            PlayerRounds AS (
                SELECT
                    pms.MapName,
                    {(includeServer ? "pms.ServerGuid" : "NULL as ServerGuid")},
                    SUM(pms.TotalRounds) as TotalRounds
                FROM PlayerMapStats pms
                WHERE pms.PlayerName = @p0
                  AND ((pms.Year > @p2) OR (pms.Year = @p2 AND pms.Month >= @p3))
                  AND pms.ServerGuid IN (SELECT Guid FROM Servers WHERE Game = @p4)
                GROUP BY {(includeServer ? "pms.MapName, pms.ServerGuid" : "pms.MapName")}
            )
            SELECT
                w.MapName,
                w.ServerGuid,
                w.Wins,
                COALESCE(r.TotalRounds, 0) as TotalRounds,
                CASE WHEN COALESCE(r.TotalRounds, 0) > 0
                     THEN ROUND(CAST(w.Wins AS REAL) / r.TotalRounds * 100, 1)
                     ELSE 0 END as WinRate
            FROM PlayerWins w
            LEFT JOIN PlayerRounds r
              ON r.MapName = w.MapName
             AND ((r.ServerGuid IS NULL AND w.ServerGuid IS NULL) OR r.ServerGuid = w.ServerGuid)
            ORDER BY w.Wins DESC, COALESCE(r.TotalRounds, 0) DESC, w.MapName ASC";

        var winData = await dbContext.Database
            .SqlQueryRaw<PlayerWinQueryResult>(
                sql,
                playerName,
                AchievementTypes.Placement,
                cutoffYear,
                cutoffMonth,
                game)
            .ToListAsync();

        var results = new List<PlayerSliceResultDto>();
        var rank = (page - 1) * pageSize + 1;

        foreach (var data in winData.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var sliceKey = data.MapName;
            var subKey = includeServer ? data.ServerGuid : null;
            var subKeyLabel = includeServer && serverNameLookup != null && data.ServerGuid != null
                ? serverNameLookup.GetValueOrDefault(data.ServerGuid, "Unknown Server")
                : null;
            var sliceLabel = includeServer && subKeyLabel != null
                ? $"{data.MapName} on {subKeyLabel}"
                : data.MapName;

            results.Add(new PlayerSliceResultDto(
                SliceKey: sliceKey,
                SubKey: subKey,
                SubKeyLabel: subKeyLabel,
                SliceLabel: sliceLabel,
                PrimaryValue: data.Wins,
                SecondaryValue: data.TotalRounds,
                Percentage: data.WinRate,
                Rank: rank++,
                TotalPlayers: 1,
                AdditionalData: new Dictionary<string, object>
                {
                    ["losses"] = Math.Max(0, data.TotalRounds - data.Wins)
                }
            ));
        }

        return results;
    }



    private async Task<List<PlayerSliceResultDto>> GetScoreByMapAsync(
        string playerName,
        List<string> serverGuids,
        int cutoffYear,
        int cutoffMonth,
        int page,
        int pageSize,
        bool includeServer,
        Dictionary<string, string>? serverNameLookup = null,
        string game = "bf1942")
    {
        var groupBy = includeServer ? "MapName, ServerGuid" : "MapName";
        var selectFields = includeServer ? "MapName, ServerGuid," : "MapName, NULL as ServerGuid,";

        // Use subquery to avoid parameter limit when there are many servers
        var sql = $@"
            SELECT
                {selectFields}
                SUM(TotalScore) as TotalScore,
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                SUM(TotalRounds) as TotalRounds
            FROM PlayerMapStats
            WHERE PlayerName = @p0
              AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
              AND ServerGuid IN (SELECT Guid FROM Servers WHERE Game = @p3)
            GROUP BY {groupBy}
            ORDER BY SUM(TotalScore) DESC";

        var scoreData = await dbContext.Database
            .SqlQueryRaw<PlayerScoreQueryResult>(sql, playerName, cutoffYear, cutoffMonth, game)
            .ToListAsync();

        var results = new List<PlayerSliceResultDto>();
        var rank = (page - 1) * pageSize + 1;

        foreach (var data in scoreData.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var sliceKey = data.MapName;
            var subKey = includeServer ? data.ServerGuid : null;
            var subKeyLabel = includeServer && serverNameLookup != null && data.ServerGuid != null
                ? serverNameLookup.GetValueOrDefault(data.ServerGuid, "Unknown Server")
                : null;
            var sliceLabel = includeServer && subKeyLabel != null
                ? $"{data.MapName} on {subKeyLabel}"
                : data.MapName;

            results.Add(new PlayerSliceResultDto(
                SliceKey: sliceKey,
                SubKey: subKey,
                SubKeyLabel: subKeyLabel,
                SliceLabel: sliceLabel,
                PrimaryValue: data.TotalScore,
                SecondaryValue: data.TotalRounds,
                Percentage: data.TotalDeaths > 0 ? Math.Round((double)data.TotalKills / data.TotalDeaths, 2) : data.TotalKills,
                Rank: rank++,
                TotalPlayers: 1, // Would need separate query for actual player count
                AdditionalData: new Dictionary<string, object>
                {
                    ["kills"] = data.TotalKills,
                    ["deaths"] = data.TotalDeaths
                }
            ));
        }

        return results;
    }

    private async Task<List<PlayerSliceResultDto>> GetKillsByMapAsync(
        string playerName,
        List<string> serverGuids,
        int cutoffYear,
        int cutoffMonth,
        int page,
        int pageSize,
        bool includeServer,
        Dictionary<string, string>? serverNameLookup = null,
        string game = "bf1942")
    {
        var groupBy = includeServer ? "MapName, ServerGuid" : "MapName";
        var selectFields = includeServer ? "MapName, ServerGuid," : "MapName, NULL as ServerGuid,";

        // Use subquery to avoid parameter limit when there are many servers
        var sql = $@"
            SELECT
                {selectFields}
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                SUM(TotalScore) as TotalScore,
                SUM(TotalRounds) as TotalRounds
            FROM PlayerMapStats
            WHERE PlayerName = @p0
              AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
              AND ServerGuid IN (SELECT Guid FROM Servers WHERE Game = @p3)
            GROUP BY {groupBy}
            ORDER BY SUM(TotalKills) DESC";

        var killData = await dbContext.Database
            .SqlQueryRaw<PlayerScoreQueryResult>(sql, playerName, cutoffYear, cutoffMonth, game)
            .ToListAsync();

        var results = new List<PlayerSliceResultDto>();
        var rank = (page - 1) * pageSize + 1;

        foreach (var data in killData.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var sliceKey = data.MapName;
            var subKey = includeServer ? data.ServerGuid : null;
            var subKeyLabel = includeServer && serverNameLookup != null && data.ServerGuid != null
                ? serverNameLookup.GetValueOrDefault(data.ServerGuid, "Unknown Server")
                : null;
            var sliceLabel = includeServer && subKeyLabel != null
                ? $"{data.MapName} on {subKeyLabel}"
                : data.MapName;

            results.Add(new PlayerSliceResultDto(
                SliceKey: sliceKey,
                SubKey: subKey,
                SubKeyLabel: subKeyLabel,
                SliceLabel: sliceLabel,
                PrimaryValue: data.TotalKills,
                SecondaryValue: data.TotalRounds,
                Percentage: data.TotalDeaths > 0 ? Math.Round((double)data.TotalKills / data.TotalDeaths, 2) : data.TotalKills,
                Rank: rank++,
                TotalPlayers: 1, // Would need separate query for actual player count
                AdditionalData: new Dictionary<string, object>
                {
                    ["score"] = data.TotalScore,
                    ["deaths"] = data.TotalDeaths
                }
            ));
        }

        return results;
    }

    private static string GetSliceDimensionName(SliceDimensionType sliceType) =>
        sliceType switch
        {
            SliceDimensionType.WinsByMap => "Wins by Map",
            SliceDimensionType.WinsByMapAndServer => "Wins by Map + Server",
            SliceDimensionType.ScoreByMap => "Score by Map",
            SliceDimensionType.ScoreByMapAndServer => "Score by Map + Server",
            SliceDimensionType.KillsByMap => "Kills by Map",
            SliceDimensionType.KillsByMapAndServer => "Kills by Map + Server",
            _ => "Unknown"
        };

    // Additional query result classes for new functionality
    private class PlayerWinQueryResult
    {
        public string MapName { get; set; } = "";
        public string? ServerGuid { get; set; }
        public int Wins { get; set; }
        public int TotalRounds { get; set; }
        public double WinRate { get; set; }
    }

    private class PlayerScoreQueryResult
    {
        public string MapName { get; set; } = "";
        public string? ServerGuid { get; set; }
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public int TotalRounds { get; set; }
    }

    private class MapRankResult
    {
        public string MapName { get; set; } = "";
        public int Rank { get; set; }
    }

    private class MonthlyRankingResult
    {
        public int Rank { get; set; }
        public int TotalPlayers { get; set; }
        public int Score { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
    }

    private class CompetitiveRankingResult
    {
        public string PlayerName { get; set; } = "";
        public string MapName { get; set; } = "";
        public int Score { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Rounds { get; set; }
        public double PlayTime { get; set; }
        public long Rank { get; set; }
        public long TotalPlayers { get; set; }
    }

    #endregion

    #region Competitive Rankings

    public async Task<PlayerCompetitiveRankingsResponse?> GetPlayerCompetitiveRankingsAsync(
        string playerName, 
        string game = "bf1942", 
        int days = 60)
    {
        var normalizedGame = NormalizeGame(game);
        // Note: Currently we don't filter by game in the rankings query since PlayerMapStats
        // doesn't have game info and joining with Servers table would be complex.
        // This returns rankings across all games.
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var startYear = cutoffDate.Year;
        var startMonth = cutoffDate.Month;

        // First check if player exists
        var playerExists = await dbContext.PlayerMapStats
            .AsNoTracking()
            .Where(p => p.PlayerName == playerName)
            .AnyAsync();

        if (!playerExists)
            return null;

        // Get current rankings for all maps the player has played
        var playerMapStats = await dbContext.Database
            .SqlQueryRaw<CompetitiveRankingResult>(@"
                WITH PlayerScores AS (
                    SELECT 
                        pms.PlayerName,
                        pms.MapName,
                        SUM(pms.TotalScore) as Score,
                        SUM(pms.TotalKills) as Kills,
                        SUM(pms.TotalDeaths) as Deaths,
                        SUM(pms.TotalRounds) as Rounds,
                        SUM(pms.TotalPlayTimeMinutes) as PlayTime
                    FROM PlayerMapStats pms
                    WHERE pms.ServerGuid = ''  -- Global stats only
                        AND ((pms.Year > {0}) OR (pms.Year = {0} AND pms.Month >= {1}))
                    GROUP BY pms.PlayerName, pms.MapName
                    HAVING SUM(pms.TotalScore) > 0
                ),
                RankedPlayers AS (
                    SELECT 
                        PlayerName,
                        MapName,
                        Score,
                        Kills,
                        Deaths,
                        Rounds,
                        PlayTime,
                        RANK() OVER (PARTITION BY MapName ORDER BY Score DESC) as Rank,
                        COUNT(*) OVER (PARTITION BY MapName) as TotalPlayers
                    FROM PlayerScores
                )
                SELECT * FROM RankedPlayers
                WHERE PlayerName = {2}
                ORDER BY Rank ASC, Score DESC",
                startYear, startMonth, playerName)
            .ToListAsync();

        if (!playerMapStats.Any())
            return null;

        // Get previous month's rankings for trend calculation
        var previousCutoff = cutoffDate.AddDays(-30);
        var prevYear = previousCutoff.Year;
        var prevMonth = previousCutoff.Month;

        // Query previous rankings separately with a simple result class
        var previousRankQuery = @"
            WITH PlayerScores AS (
                SELECT 
                    pms.PlayerName,
                    pms.MapName,
                    SUM(pms.TotalScore) as Score
                FROM PlayerMapStats pms
                WHERE pms.ServerGuid = ''
                    AND ((pms.Year > {0}) OR (pms.Year = {0} AND pms.Month >= {1}))
                    AND ((pms.Year < {2}) OR (pms.Year = {2} AND pms.Month < {3}))
                GROUP BY pms.PlayerName, pms.MapName
            ),
            RankedPlayers AS (
                SELECT 
                    PlayerName,
                    MapName,
                    RANK() OVER (PARTITION BY MapName ORDER BY Score DESC) as Rank
                FROM PlayerScores
            )
            SELECT MapName, CAST(Rank AS INT) as Rank
            FROM RankedPlayers
            WHERE PlayerName = {4}";

        var previousRankingsData = await dbContext.Database
            .SqlQueryRaw<MapRankResult>(previousRankQuery,
                prevYear, prevMonth, startYear, startMonth, playerName)
            .ToListAsync();

        var previousRankings = previousRankingsData.ToDictionary(r => r.MapName, r => r.Rank);

        // Build response
        var mapRankings = playerMapStats.Select(stat =>
        {
            var percentile = 100.0 * (stat.TotalPlayers - stat.Rank + 1) / stat.TotalPlayers;
            var kdRatio = stat.Deaths > 0 ? (double)stat.Kills / stat.Deaths : stat.Kills;
            
            var trend = "new";
            int? previousRank = null;
            
            if (previousRankings.TryGetValue(stat.MapName, out var prevRank))
            {
                previousRank = prevRank;
                if ((int)stat.Rank < prevRank) trend = "up";
                else if ((int)stat.Rank > prevRank) trend = "down";
                else trend = "stable";
            }

            return new MapRankingDto(
                MapName: stat.MapName,
                Rank: (int)stat.Rank,
                TotalPlayers: (int)stat.TotalPlayers,
                Percentile: Math.Round(percentile, 1),
                TotalScore: stat.Score,
                TotalKills: stat.Kills,
                TotalDeaths: stat.Deaths,
                KdRatio: Math.Round(kdRatio, 2),
                TotalRounds: stat.Rounds,
                PlayTimeMinutes: Math.Round(stat.PlayTime, 1),
                Trend: trend,
                PreviousRank: previousRank
            );
        }).ToList();

        // Calculate summary
        var avgPercentile = mapRankings.Average(r => r.Percentile);
        var bestRank = mapRankings.Min(r => r.Rank);
        var bestMap = mapRankings.FirstOrDefault(r => r.Rank == bestRank)?.MapName;

        var percentileCategory = avgPercentile switch
        {
            >= 99 => "elite",
            >= 95 => "master",
            >= 90 => "expert",
            >= 75 => "veteran",
            _ => "regular"
        };

        var summary = new RankingSummaryDto(
            TotalMapsPlayed: mapRankings.Count,
            Top1Rankings: mapRankings.Count(r => r.Rank == 1),
            Top10Rankings: mapRankings.Count(r => r.Rank <= 10),
            Top25Rankings: mapRankings.Count(r => r.Rank <= 25),
            Top100Rankings: mapRankings.Count(r => r.Rank <= 100),
            AveragePercentile: Math.Round(avgPercentile, 1),
            BestRankedMap: bestMap,
            BestRank: bestRank,
            PercentileCategory: percentileCategory
        );

        return new PlayerCompetitiveRankingsResponse(
            PlayerName: playerName,
            Game: normalizedGame,
            MapRankings: mapRankings,
            Summary: summary,
            DateRange: new DateRangeDto(days, cutoffDate, DateTime.UtcNow)
        );
    }

    public async Task<RankingTimelineResponse?> GetPlayerRankingTimelineAsync(
        string playerName,
        string? mapName = null,
        string game = "bf1942",
        int months = 12)
    {
        var normalizedGame = NormalizeGame(game);
        // Note: Currently we don't filter by game in the timeline query since PlayerMapStats
        // doesn't have game info. This returns timeline across all games.
        var currentDate = DateTime.UtcNow;

        // Build timeline going back N months
        var timeline = new List<RankingSnapshotDto>();

        for (int i = 0; i < months; i++)
        {
            var targetDate = currentDate.AddMonths(-i);
            var year = targetDate.Year;
            var month = targetDate.Month;
            var monthLabel = targetDate.ToString("MMM yyyy");

            // Query rankings for this specific month
            var query = @"
                WITH PlayerScores AS (
                    SELECT 
                        pms.PlayerName,
                        pms.MapName,
                        SUM(pms.TotalScore) as Score,
                        SUM(pms.TotalKills) as Kills,
                        SUM(pms.TotalDeaths) as Deaths
                    FROM PlayerMapStats pms
                    WHERE pms.ServerGuid = ''
                        AND pms.Year = {0}
                        AND pms.Month = {1}";

            if (!string.IsNullOrEmpty(mapName))
            {
                query += " AND pms.MapName = {2}";
            }

            query += @"
                    GROUP BY pms.PlayerName, pms.MapName
                    HAVING SUM(pms.TotalScore) > 0
                ),
                RankedPlayers AS (
                    SELECT 
                        PlayerName,
                        Score,
                        Kills,
                        Deaths,
                        RANK() OVER (ORDER BY Score DESC) as Rank,
                        COUNT(*) OVER () as TotalPlayers
                    FROM PlayerScores
                )
                SELECT 
                    CAST(Rank AS INT) as Rank,
                    CAST(TotalPlayers AS INT) as TotalPlayers,
                    CAST(Score AS INT) as Score,
                    CAST(Kills AS INT) as Kills,
                    CAST(Deaths AS INT) as Deaths
                FROM RankedPlayers
                WHERE PlayerName = {3}";

            var parameters = string.IsNullOrEmpty(mapName)
                ? new object[] { year, month, playerName }
                : new object[] { year, month, mapName, playerName };

            var monthData = await dbContext.Database
                .SqlQueryRaw<MonthlyRankingResult>(query, parameters)
                .FirstOrDefaultAsync();

            if (monthData != null)
            {
                var percentile = 100.0 * (monthData.TotalPlayers - monthData.Rank + 1) / monthData.TotalPlayers;
                var kdRatio = monthData.Deaths > 0 ? (double)monthData.Kills / monthData.Deaths : monthData.Kills;

                timeline.Add(new RankingSnapshotDto(
                    Year: year,
                    Month: month,
                    MonthLabel: monthLabel,
                    Rank: monthData.Rank,
                    TotalPlayers: monthData.TotalPlayers,
                    Percentile: Math.Round(percentile, 1),
                    TotalScore: monthData.Score,
                    KdRatio: Math.Round(kdRatio, 2),
                    HasData: true
                ));
            }
            else
            {
                // No data for this month
                timeline.Add(new RankingSnapshotDto(
                    Year: year,
                    Month: month,
                    MonthLabel: monthLabel,
                    Rank: 0,
                    TotalPlayers: 0,
                    Percentile: 0,
                    TotalScore: 0,
                    KdRatio: 0,
                    HasData: false
                ));
            }
        }

        // Return null if no data at all
        if (timeline.All(t => !t.HasData))
            return null;

        return new RankingTimelineResponse(
            PlayerName: playerName,
            MapName: mapName,
            Game: normalizedGame,
            Timeline: timeline
        );
    }

    #endregion

    #region Player Map Details

    public async Task<PlayerMapDetailResponse?> GetPlayerMapStatsAsync(
        string playerName,
        string mapName,
        string game = "bf1942",
        int days = 60)
    {
        var normalizedGame = NormalizeGame(game);
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var cutoffYear = cutoffDate.Year;
        var cutoffMonth = cutoffDate.Month;

        // Get aggregated stats for the map - try global first, then aggregate from servers
        var globalStats = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName
                && pms.MapName == mapName
                && pms.ServerGuid == "" // Global stats
                && ((pms.Year > cutoffYear) || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .GroupBy(pms => new { pms.PlayerName, pms.MapName })
            .Select(g => new PlayerMapAggregatedStats(
                TotalScore: g.Sum(x => x.TotalScore),
                TotalKills: g.Sum(x => x.TotalKills),
                TotalDeaths: g.Sum(x => x.TotalDeaths),
                TotalRounds: g.Sum(x => x.TotalRounds),
                PlayTimeMinutes: g.Sum(x => x.TotalPlayTimeMinutes)
            ))
            .FirstOrDefaultAsync();

        // If no global stats, try aggregating from server-specific stats
        var aggregatedStats = globalStats;
        if (aggregatedStats == null)
        {
            aggregatedStats = await dbContext.PlayerMapStats
                .Where(pms => pms.PlayerName == playerName
                    && pms.MapName == mapName
                    && pms.ServerGuid != "" // Server-specific stats
                    && ((pms.Year > cutoffYear) || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
                .GroupBy(pms => new { pms.PlayerName, pms.MapName })
                .Select(g => new PlayerMapAggregatedStats(
                    TotalScore: g.Sum(x => x.TotalScore),
                    TotalKills: g.Sum(x => x.TotalKills),
                    TotalDeaths: g.Sum(x => x.TotalDeaths),
                    TotalRounds: g.Sum(x => x.TotalRounds),
                    PlayTimeMinutes: g.Sum(x => x.TotalPlayTimeMinutes)
                ))
                .FirstOrDefaultAsync();
        }

        if (aggregatedStats == null)
        {
            return null;
        }

        // Get breakdown by server
        var serverBreakdown = await dbContext.PlayerMapStats
            .Where(pms => pms.PlayerName == playerName
                && pms.MapName == mapName
                && pms.ServerGuid != "" // Exclude global stats
                && ((pms.Year > cutoffYear) || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .GroupBy(pms => pms.ServerGuid)
            .Select(g => new
            {
                ServerGuid = g.Key,
                Score = g.Sum(x => x.TotalScore),
                Kills = g.Sum(x => x.TotalKills),
                Deaths = g.Sum(x => x.TotalDeaths),
                Rounds = g.Sum(x => x.TotalRounds),
                PlayTime = g.Sum(x => x.TotalPlayTimeMinutes)
            })
            .ToListAsync();

        // Get server names
        var serverGuids = serverBreakdown.Select(s => s.ServerGuid).ToList();
        var serverNames = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .Select(s => new { s.Guid, s.Name })
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        var serverBreakdownList = serverBreakdown
            .Select(s => new PlayerMapServerBreakdown(
                ServerGuid: s.ServerGuid,
                ServerName: serverNames.GetValueOrDefault(s.ServerGuid, s.ServerGuid),
                Score: s.Score,
                Kills: s.Kills,
                Deaths: s.Deaths,
                Rounds: s.Rounds,
                PlayTime: s.PlayTime
            ))
            .OrderByDescending(s => s.Score)
            .ToList();

        return new PlayerMapDetailResponse(
            PlayerName: playerName,
            MapName: mapName,
            Game: normalizedGame,
            AggregatedStats: aggregatedStats,
            ServerBreakdown: serverBreakdownList,
            DateRange: new DateRangeDto(days, cutoffDate, DateTime.UtcNow)
        );
    }

    #endregion
}
