using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.ServerStats;

public class ServerStatsService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly PrometheusService _prometheusService;

    public ServerStatsService(PlayerTrackerDbContext dbContext, PrometheusService prometheusService)
    {
        _dbContext = dbContext;
        _prometheusService = prometheusService;
    }

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

        // 2. Get popular maps statistics
        var popularMaps = sessions
            .GroupBy(s => s.MapName)
            .Select(g => new PopularMap
            {
                MapName = g.Key,
                // Count distinct players who played on this map
                PlayerCount = g.Select(s => s.PlayerName).Distinct().Count(),
                // Calculate total time played on this map
                TotalMinutesPlayed = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                // Count the number of sessions on this map
                TotalSessions = g.Count()
            })
            .OrderByDescending(m => m.PlayerCount) // Primary sort by player count
            .ThenByDescending(m => m.TotalMinutesPlayed) // Secondary sort by time played
            .Take(10)
            .ToList();

        statistics.PopularMaps = popularMaps;

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