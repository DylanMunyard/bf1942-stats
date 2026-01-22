using api.DataExplorer.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
            )
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
            TotalDeaths: p.TotalDeaths,
            KdRatio: p.KdRatio,
            KillsPerMinute: p.KillsPerMinute,
            TotalRounds: p.TotalRounds,
            PlayTimeMinutes: p.TotalPlayTimeMinutes
        )).ToList();

        // Sort and get top 10 for each category
        var topByScore = allEntries
            .OrderByDescending(e => e.TotalScore)
            .Take(10)
            .ToList();

        var topByKills = allEntries
            .OrderByDescending(e => e.TotalKills)
            .Take(10)
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

    public async Task<PlayerMapRankingsResponse?> GetPlayerMapRankingsAsync(string playerName, string game = "bf1942", int days = 60)
    {
        var normalizedGame = NormalizeGame(game);

        // Calculate date range
        var toDate = DateTime.UtcNow;
        var fromDate = toDate.AddDays(-days);
        var cutoffYear = fromDate.Year;
        var cutoffMonth = fromDate.Month;

        // Get server GUIDs and names for the specified game
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
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
            r => r.Rank
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
            "kdratio" => ("KdRatio", "KdRatio DESC"),
            "killrate" => ("KillsPerMinute", "KillsPerMinute DESC"),
            _ => ("TotalScore", "TotalScore DESC") // default to score
        };

        // Query player rankings with pagination
        var rankingsSql = $@"
            SELECT
                ROW_NUMBER() OVER (ORDER BY {orderByClause}) as Rank,
                PlayerName,
                TotalScore,
                TotalKills,
                TotalDeaths,
                KdRatio,
                KillsPerMinute,
                TotalRounds,
                TotalPlayTimeMinutes as PlayTimeMinutes,
                UniqueServers
            FROM (
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
                    SUM(TotalPlayTimeMinutes) as TotalPlayTimeMinutes,
                    COUNT(DISTINCT ServerGuid) as UniqueServers
                FROM PlayerMapStats
                WHERE MapName = @p0
                  AND ((Year > @p1) OR (Year = @p1 AND Month >= @p2))
                  AND ServerGuid IN ({guidParams})
                  {playerFilter}
                GROUP BY PlayerName
                HAVING SUM(TotalRounds) >= 3
            )
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
            UniqueServers: r.UniqueServers
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
            .Select(g => new {
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
        public string MapName { get; set; } = "";
        public string ServerGuid { get; set; } = "";
        public int Rank { get; set; }
        public int TotalScore { get; set; }
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

        stats.Add(new ServerEngagementStat
        {
            Value = totalRounds.ToString("N0"),
            Label = "rounds played",
            Context = $"Total matches in {timeLabel}"
        });

        // Stat 2: Unique maps played in selected period
        var uniqueMaps = await dbContext.ServerMapStats
            .Where(sms => sms.ServerGuid == serverGuid &&
                         (sms.Year > cutoffYear || (sms.Year == cutoffYear && sms.Month >= cutoffMonth)))
            .Select(sms => sms.MapName)
            .Distinct()
            .CountAsync();

        stats.Add(new ServerEngagementStat
        {
            Value = uniqueMaps.ToString("N0"),
            Label = "maps in rotation",
            Context = $"Active in {timeLabel}"
        });

        // Stat 3: Peak concurrent players from activity patterns
        var peakPlayers = await dbContext.MapServerHourlyPatterns
            .Where(mshp => mshp.ServerGuid == serverGuid)
            .MaxAsync(mshp => (double?)mshp.AvgPlayers) ?? 0;

        stats.Add(new ServerEngagementStat
        {
            Value = Math.Round(peakPlayers).ToString("N0"),
            Label = "peak concurrent players",
            Context = "Highest count recorded"
        });

        // Stat 4: Total unique players in selected period (bonus stat)
        var totalPlayers = await dbContext.PlayerMapStats
            .Where(pms => pms.ServerGuid == serverGuid &&
                         (pms.Year > cutoffYear || (pms.Year == cutoffYear && pms.Month >= cutoffMonth)))
            .Select(pms => pms.PlayerName)
            .Distinct()
            .CountAsync();

        stats.Add(new ServerEngagementStat
        {
            Value = totalPlayers.ToString("N0"),
            Label = "unique players",
            Context = $"Active in {timeLabel}"
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
            stats.Add(new PlayerEngagementStat
            {
                Value = periodStats.TotalKills.ToString("N0"),
                Label = "kills earned",
                Context = $"{periodStats.TotalRounds:N0} rounds in {timeLabel}"
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
            stats.Add(new PlayerEngagementStat
            {
                Value = uniqueServers.ToString("N0"),
                Label = "servers active on",
                Context = $"In {timeLabel}"
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
            stats.Add(new PlayerEngagementStat
            {
                Value = uniqueMaps.ToString("N0"),
                Label = "maps played",
                Context = $"In {timeLabel}"
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
            stats.Add(new PlayerEngagementStat
            {
                Value = bestKdStats.KdRatio.ToString("N2"),
                Label = "best K/D ratio",
                Context = $"{bestKdStats.TotalKills}K/{bestKdStats.TotalDeaths}D in {timeLabel}"
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
            stats.Add(new PlayerEngagementStat
            {
                Value = favoriteMap.MapName,
                Label = "most active map",
                Context = $"{favoriteMap.TotalRounds:N0} rounds in {timeLabel}"
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
                stats.Add(new PlayerEngagementStat
                {
                    Value = allTimeFavorite.MapName,
                    Label = "all-time favorite",
                    Context = $"{allTimeFavorite.TotalRounds:N0} total rounds"
                });
            }
        }

        // Shuffle the stats for randomization and take 3 (or fewer if not enough data)
        var availableStats = stats.Where(s => !string.IsNullOrEmpty(s.Value) && s.Value != "0").ToList();
        var randomizedStats = availableStats.OrderBy(_ => random.Next()).Take(Math.Min(3, availableStats.Count)).ToArray();

        // If we don't have enough stats, add some defaults
        if (randomizedStats.Length < 3)
        {
            var defaultStats = new[]
            {
                new PlayerEngagementStat { Value = "0", Label = "recent activity", Context = "Keep playing!" }
            };

            var combinedStats = randomizedStats.Concat(defaultStats).Take(3).ToArray();
            randomizedStats = combinedStats;
        }

        return new PlayerEngagementStatsDto { Stats = randomizedStats };
    }

    #endregion
}
