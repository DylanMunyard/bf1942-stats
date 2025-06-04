using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.ServerStats;

public class ServerStatsService(PlayerTrackerDbContext dbContext, PrometheusService prometheusService)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly PrometheusService _prometheusService = prometheusService;

    public async Task<ServerStatistics> GetServerStatistics(
        string serverName, 
        string game,
        int daysToAnalyze = 7)
    {
        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new ServerStatistics { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Create the statistics object
        var statistics = new ServerStatistics
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Get sessions for this server within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.ServerGuid == server.Guid && ps.StartTime >= startPeriod && ps.LastSeenTime <= endPeriod)
            .Include(s => s.Player)
            .ToListAsync();

        // 1. Get most active players by time played
        var mostActivePlayers = sessions
            .GroupBy(s => s.PlayerName)
            .Select(g => new PlayerActivity
            {
                PlayerName = g.Key,
                MinutesPlayed = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .OrderByDescending(p => p.MinutesPlayed)
            .Take(10)
            .ToList();

        statistics.MostActivePlayersByTime = mostActivePlayers;

        // 3. Get top 10 scores in the period
        var topScores = sessions
            .OrderByDescending(s => s.TotalScore)
            .Take(10)
            .Select(s => new TopScore
            {
                PlayerName = s.PlayerName,
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                MapName = s.MapName,
                Timestamp = s.LastSeenTime,
                SessionId = s.SessionId
            })
            .ToList();

        statistics.TopScores = topScores;
        
        var playerHistory = await _prometheusService.GetServerPlayersHistory(serverName, game, daysToAnalyze);
        if (playerHistory != null &&
            playerHistory.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            playerHistory.Data.Result.Count > 0)
        {
            statistics.PlayerCountMetrics = playerHistory.Data.Result[0].Values;
        }

        return statistics;
    }

    public async Task<PagedResult<ServerRanking>> GetServerRankings(string serverName, int page = 1, int pageSize = 100)
    {
        if (page < 1)
            throw new ArgumentException("Page number must be at least 1");
        
        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Page size must be between 1 and 100");

        // Get total statistics for the server
        var totalStats = await _dbContext.ServerPlayerRankings
            .Where(sr => sr.Server.Name == serverName)
            .GroupBy(sr => sr.ServerGuid)
            .Select(g => new
            {
                ServerGuid = g.Key,
                TotalMinutesPlayed = g.Sum(r => r.TotalPlayTimeMinutes),
                TotalSessions = g.Count(),
                TotalPlayers = g.Select(r => r.PlayerName).Distinct().Count(),
                LastPlayed = g.Max(r => r.LastUpdated)
            })
            .ToListAsync();

        var query = _dbContext.ServerPlayerRankings
            .Where(sr => sr.Server.Name == serverName)
            .Include(sr => sr.Server)
            .OrderBy(sr => sr.Rank)
            .Select(sr => new ServerRanking
            {
                Rank = sr.Rank,
                ServerGuid = sr.ServerGuid,
                ServerName = sr.Server.Name,
                PlayerName = sr.PlayerName,
                HighestScore = sr.HighestScore,
                TotalKills = sr.TotalKills,
                TotalDeaths = sr.TotalDeaths,
                KDRatio = sr.KDRatio,
                TotalPlayTimeMinutes = sr.TotalPlayTimeMinutes,
                LastUpdated = sr.LastUpdated
            });

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Create server context info
        var serverContext = new ServerContextInfo
        {
            ServerGuid = items.FirstOrDefault()?.ServerGuid,
            ServerName = items.FirstOrDefault()?.ServerName,
            TotalMinutesPlayed = totalStats.Sum(s => s.TotalMinutesPlayed),
            TotalSessions = totalStats.Sum(s => s.TotalSessions),
            TotalPlayers = totalStats.Sum(s => s.TotalPlayers),
            LastPlayed = totalStats.Max(s => s.LastPlayed)
        };

        return new PagedResult<ServerRanking>
        {
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
            Items = items,
            TotalItems = totalItems,
            ServerContext = serverContext
        };
    }

    public async Task<MapStatistics> GetMapStatistics(string serverName, string mapName, int daysToAnalyze = 7)
    {
        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new MapStatistics
            {
                ServerName = serverName,
                MapName = mapName,
                StartPeriod = startPeriod,
                EndPeriod = endPeriod
            };

        // Create the statistics object
        var statistics = new MapStatistics
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            MapName = mapName,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Get sessions for this server and map within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.ServerGuid == server.Guid &&
                         ps.MapName == mapName &&
                         ps.StartTime >= startPeriod &&
                         ps.LastSeenTime <= endPeriod)
            .Include(s => s.Player)
            .ToListAsync();

        // If no sessions match this map, return empty stats
        if (!sessions.Any())
        {
            return statistics;
        }

        // Calculate map statistics
        statistics.PlayerCount = sessions.Select(s => s.PlayerName).Distinct().Count();
        statistics.TotalMinutesPlayed =
            sessions.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes));
        statistics.TotalSessions = sessions.Count;

        // Get most active players by time played on this map
        var mostActivePlayers = sessions
            .GroupBy(s => s.PlayerName)
            .Select(g => new PlayerActivity
            {
                PlayerName = g.Key,
                MinutesPlayed = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .OrderByDescending(p => p.MinutesPlayed)
            .Take(10)
            .ToList();

        statistics.MostActivePlayersByTime = mostActivePlayers;

        // Get top 10 scores on this map in the period
        var topScores = sessions
            .OrderByDescending(s => s.TotalScore)
            .Take(10)
            .Select(s => new TopScore
            {
                PlayerName = s.PlayerName,
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                MapName = s.MapName,
                Timestamp = s.LastSeenTime,
                SessionId = s.SessionId
            })
            .ToList();

        statistics.TopScores = topScores;

        return statistics;
    }
}