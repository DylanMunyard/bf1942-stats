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

    public async Task<ServerListResponse> GetServersAsync(string game = "bf1942")
    {
        var normalizedGame = NormalizeGame(game);

        // Get servers for the specified game from the database
        // The IsOnline flag is maintained by the background job that polls the BfList API
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => new { s.Guid, s.Name, s.Game, s.Country, s.MaxPlayers, s.CurrentNumPlayers, s.IsOnline })
            .ToListAsync();

        if (servers.Count == 0)
            return new ServerListResponse([], 0);

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

        var serverSummaries = servers.Select(s =>
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

        return new ServerListResponse(serverSummaries, serverSummaries.Count);
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
            DateRange: dateRange
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

    #endregion
}
