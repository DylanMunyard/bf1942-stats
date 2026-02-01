using System.ComponentModel;
using System.Text.Json;
using api.PlayerTracking;
using api.Servers;
using api.Servers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace api.AI.Plugins;

/// <summary>
/// Semantic Kernel plugin for server statistics queries.
/// Provides server information, leaderboards, and server-specific insights.
/// </summary>
public class ServerStatsPlugin(
    IServerStatsService serverStatsService,
    PlayerTrackerDbContext dbContext,
    ILogger<ServerStatsPlugin> logger)
{
    [KernelFunction("GetServerLeaderboard")]
    [Description("Gets the top players on a specific server for a given time period.")]
    public async Task<string> GetServerLeaderboardAsync(
        [Description("The server name to get leaderboard for")] string serverName,
        [Description("Number of days to look back (default: 30)")] int days = 30)
    {
        logger.LogDebug("AI requesting server leaderboard: {ServerName}, days: {Days}", serverName, days);

        try
        {
            var leaderboards = await serverStatsService.GetServerLeaderboards(serverName, days);

            return JsonSerializer.Serialize(new
            {
                ServerName = serverName,
                DaysAnalyzed = days,
                MostActivePlayers = leaderboards.MostActivePlayersByTime?.Take(10).Select(p => new
                {
                    p.PlayerName,
                    PlayTimeHours = Math.Round(p.MinutesPlayed / 60.0, 1),
                    p.TotalKills,
                    p.TotalDeaths,
                    p.KdRatio
                }).ToList(),
                TopScores = leaderboards.TopScores?.Take(5).Select(s => new
                {
                    s.PlayerName,
                    s.Score,
                    s.Kills,
                    s.Deaths,
                    s.MapName,
                    Date = s.Timestamp.ToString("yyyy-MM-dd")
                }).ToList(),
                TopKdRatios = leaderboards.TopKDRatios?.Take(5).Select(k => new
                {
                    k.PlayerName,
                    k.KDRatio,
                    k.Kills,
                    k.Deaths,
                    k.TotalRounds
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get leaderboard for server: {ServerName}", serverName);
            return $"Could not find leaderboard data for server '{serverName}'. The server may not exist or have no recent activity.";
        }
    }

    [KernelFunction("GetServerInfo")]
    [Description("Gets detailed information about a server including activity statistics and player counts.")]
    public async Task<string> GetServerInfoAsync(
        [Description("The server name to look up")] string serverName,
        [Description("Number of days to analyze (default: 7)")] int days = 7)
    {
        logger.LogDebug("AI requesting server info: {ServerName}, days: {Days}", serverName, days);

        try
        {
            var stats = await serverStatsService.GetServerStatistics(serverName, days);

            if (string.IsNullOrEmpty(stats.ServerGuid))
            {
                return $"Server '{serverName}' not found.";
            }

            // Get activity data from rounds
            var startDate = DateTime.UtcNow.AddDays(-days);
            var roundStats = await dbContext.Rounds
                .AsNoTracking()
                .Where(r => r.ServerGuid == stats.ServerGuid && r.StartTime >= startDate)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    TotalRounds = g.Count(),
                    TotalPlayers = g.Sum(r => r.ParticipantCount ?? 0),
                    AvgPlayers = g.Average(r => r.ParticipantCount ?? 0),
                    PeakPlayers = g.Max(r => r.ParticipantCount ?? 0)
                })
                .OrderBy(x => x.TotalRounds)
                .FirstOrDefaultAsync();

            // Get map insights
            var mapsInsights = await serverStatsService.GetServerMapsInsights(serverName, days);

            return JsonSerializer.Serialize(new
            {
                stats.ServerName,
                stats.GameId,
                stats.CurrentMap,
                Address = $"{stats.ServerIp}:{stats.ServerPort}",
                DaysAnalyzed = days,
                TotalRounds = roundStats?.TotalRounds ?? 0,
                AvgPlayersPerRound = Math.Round(roundStats?.AvgPlayers ?? 0, 1),
                PeakPlayerCount = roundStats?.PeakPlayers ?? 0,
                TopMaps = mapsInsights.Maps?.Take(5).Select(m => new
                {
                    m.MapName,
                    AvgPlayers = Math.Round(m.AveragePlayerCount, 1),
                    PlayTimeHours = Math.Round(m.TotalPlayTime / 60.0, 1)
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get info for server: {ServerName}", serverName);
            return $"Could not find information for server '{serverName}'. The server may not exist or have no recent activity.";
        }
    }

    [KernelFunction("SearchServers")]
    [Description("Search for servers by partial name.")]
    public async Task<string> SearchServersAsync(
        [Description("The partial server name to search for")] string query)
    {
        logger.LogDebug("AI searching servers with query: {Query}", query);

        if (query.Length < 2)
        {
            return "Search query must be at least 2 characters long.";
        }

        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Name.Contains(query))
            .OrderBy(s => s.Name)
            .Take(10)
            .Select(s => new
            {
                s.Name,
                s.Guid,
                s.GameId,
                s.Ip,
                s.Port
            })
            .ToListAsync();

        if (servers.Count == 0)
        {
            return $"No servers found matching '{query}'.";
        }

        return JsonSerializer.Serialize(new
        {
            MatchCount = servers.Count,
            Servers = servers.Select(s => new
            {
                ServerName = s.Name,
                s.GameId,
                Address = $"{s.Ip}:{s.Port}"
            }).ToList()
        });
    }

    [KernelFunction("GetServerByGuid")]
    [Description("Gets server information by its GUID.")]
    public async Task<string> GetServerByGuidAsync(
        [Description("The server GUID")] string serverGuid)
    {
        logger.LogDebug("AI requesting server by GUID: {ServerGuid}", serverGuid);

        var server = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Guid == serverGuid)
            .OrderBy(s => s.Guid)
            .Select(s => new
            {
                s.Name,
                s.Guid,
                s.GameId,
                s.Ip,
                s.Port
            })
            .FirstOrDefaultAsync();

        if (server == null)
        {
            return $"No server found with GUID '{serverGuid}'.";
        }

        // Get recent activity
        var recentActivity = await dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.ServerGuid == serverGuid)
            .OrderByDescending(r => r.StartTime)
            .Take(5)
            .Select(r => new
            {
                r.MapName,
                r.GameType,
                StartTime = r.StartTime,
                r.ParticipantCount,
                r.DurationMinutes
            })
            .ToListAsync();

        return JsonSerializer.Serialize(new
        {
            ServerName = server.Name,
            server.GameId,
            Address = $"{server.Ip}:{server.Port}",
            RecentRounds = recentActivity.Select(r => new
            {
                r.MapName,
                r.GameType,
                StartTime = r.StartTime.ToString("yyyy-MM-dd HH:mm UTC"),
                Players = r.ParticipantCount,
                DurationMinutes = r.DurationMinutes
            }).ToList()
        });
    }

    [KernelFunction("GetServerMapStats")]
    [Description("Gets map statistics for a specific server, showing which maps are most popular and player performance on each.")]
    public async Task<string> GetServerMapStatsAsync(
        [Description("The server name to get map stats for")] string serverName,
        [Description("Number of days to analyze (default: 30)")] int days = 30)
    {
        logger.LogDebug("AI requesting server map stats: {ServerName}, days: {Days}", serverName, days);

        try
        {
            var mapsInsights = await serverStatsService.GetServerMapsInsights(serverName, days);

            return JsonSerializer.Serialize(new
            {
                ServerName = serverName,
                DaysAnalyzed = days,
                TotalMapsPlayed = mapsInsights.Maps?.Count ?? 0,
                Maps = mapsInsights.Maps?.Take(10).Select(m => new
                {
                    m.MapName,
                    AvgPlayers = Math.Round(m.AveragePlayerCount, 1),
                    m.PeakPlayerCount,
                    TotalPlayTimeHours = Math.Round(m.TotalPlayTime / 60.0, 1)
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get map stats for server: {ServerName}", serverName);
            return $"Could not find map statistics for server '{serverName}'.";
        }
    }
}
