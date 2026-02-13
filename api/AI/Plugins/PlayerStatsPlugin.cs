using System.ComponentModel;
using System.Text.Json;
using api.Analytics.Models;
using api.DataExplorer;
using api.Players.Models;
using api.PlayerStats;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace api.AI.Plugins;

/// <summary>
/// Semantic Kernel plugin for player statistics queries.
/// Wraps ISqlitePlayerStatsService and IDataExplorerService methods.
/// </summary>
public class PlayerStatsPlugin(
    ISqlitePlayerStatsService playerStatsService,
    IDataExplorerService dataExplorerService,
    PlayerTrackerDbContext dbContext,
    ILogger<PlayerStatsPlugin> logger)
{
    [KernelFunction("GetPlayerLifetimeStats")]
    [Description("Gets lifetime statistics for a player including total kills, deaths, score, K/D ratio, and playtime.")]
    public async Task<string> GetPlayerLifetimeStatsAsync(
        [Description("The exact player name to look up")] string playerName,
        [Description("Number of days to look back for stats (default 30). Increase if the player hasn't been online recently.")] int lookBackDays = 30)
    {
        logger.LogDebug("AI requesting lifetime stats for player: {PlayerName}, lookBackDays: {LookBackDays}", playerName, lookBackDays);

        var stats = await playerStatsService.GetPlayerStatsAsync(playerName, lookBackDays);
        if (stats == null)
        {
            return $"No statistics found for player '{playerName}'. The player may not exist or hasn't played recently.";
        }

        var playTimeHours = Math.Round(stats.TotalPlayTimeMinutes / 60, 1);
        return JsonSerializer.Serialize(new
        {
            stats.PlayerName,
            stats.TotalRounds,
            stats.TotalKills,
            stats.TotalDeaths,
            stats.TotalScore,
            PlayTimeHours = playTimeHours,
            AvgScorePerRound = Math.Round(stats.AvgScorePerRound, 1),
            KdRatio = Math.Round(stats.KdRatio, 2),
            KillsPerMinute = Math.Round(stats.KillRate, 2),
            FirstSeen = stats.FirstRoundTime.ToString("yyyy-MM-dd"),
            LastSeen = stats.LastRoundTime.ToString("yyyy-MM-dd")
        });
    }

    [KernelFunction("GetPlayerServerInsights")]
    [Description("Gets which servers a player frequents and their performance on each server. Only includes servers where the player has 10+ hours of playtime.")]
    public async Task<string> GetPlayerServerInsightsAsync(
        [Description("The exact player name to look up")] string playerName,
        [Description("Number of days to look back for stats (default 30). Increase if the player hasn't been online recently.")] int lookBackDays = 30)
    {
        logger.LogDebug("AI requesting server insights for player: {PlayerName}, lookBackDays: {LookBackDays}", playerName, lookBackDays);

        var insights = await playerStatsService.GetPlayerServerInsightsAsync(playerName, lookBackDays);
        if (insights.Count == 0)
        {
            return $"No server insights found for player '{playerName}'. They may not have 10+ hours on any single server.";
        }

        var result = insights.Select(s => new
        {
            s.ServerName,
            s.GameId,
            PlayTimeHours = Math.Round(s.TotalMinutes / 60, 1),
            s.TotalKills,
            s.TotalDeaths,
            KdRatio = s.KdRatio,
            KillsPerMinute = Math.Round(s.KillsPerMinute, 2),
            s.TotalRounds,
            s.HighestScore
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [KernelFunction("GetPlayerMapStats")]
    [Description("Gets a player's performance broken down by map for a specific time period.")]
    public async Task<string> GetPlayerMapStatsAsync(
        [Description("The exact player name to look up")] string playerName,
        [Description("Time period: 'Last30Days', 'ThisYear', or 'LastYear'")] string period = "Last30Days")
    {
        logger.LogDebug("AI requesting map stats for player: {PlayerName}, period: {Period}", playerName, period);

        var timePeriod = period.ToLowerInvariant() switch
        {
            "thisyear" => TimePeriod.ThisYear,
            "lastyear" => TimePeriod.LastYear,
            _ => TimePeriod.Last30Days
        };

        var mapStats = await playerStatsService.GetPlayerMapStatsAsync(playerName, timePeriod);
        if (mapStats.Count == 0)
        {
            return $"No map statistics found for player '{playerName}' in the {period} period.";
        }

        var result = mapStats.Take(10).Select(m => new
        {
            m.MapName,
            m.TotalScore,
            m.TotalKills,
            m.TotalDeaths,
            KdRatio = m.TotalDeaths > 0 ? Math.Round((double)m.TotalKills / m.TotalDeaths, 2) : m.TotalKills,
            m.SessionsPlayed,
            PlayTimeHours = Math.Round(m.TotalPlayTimeMinutes / 60.0, 1)
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [KernelFunction("GetPlayerBestScores")]
    [Description("Gets a player's top 3 best scores for this week, last 30 days, and all time.")]
    public async Task<string> GetPlayerBestScoresAsync(
        [Description("The exact player name to look up")] string playerName,
        [Description("Number of days to look back for stats (default 30). Increase if the player hasn't been online recently.")] int lookBackDays = 30)
    {
        logger.LogDebug("AI requesting best scores for player: {PlayerName}, lookBackDays: {LookBackDays}", playerName, lookBackDays);

        var bestScores = await playerStatsService.GetPlayerBestScoresAsync(playerName, lookBackDays);

        var formatScores = (List<BestScoreDetail> scores) => scores.Select(s => new
        {
            s.Score,
            s.Kills,
            s.Deaths,
            s.MapName,
            s.ServerName,
            Date = s.Timestamp.ToString("yyyy-MM-dd")
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            ThisWeek = formatScores(bestScores.ThisWeek),
            Last30Days = formatScores(bestScores.Last30Days),
            AllTime = formatScores(bestScores.AllTime)
        });
    }

    [KernelFunction("GetTopPlayersByKDRatio")]
    [Description("Gets the top players ranked by K/D ratio across ALL servers (global leaderboard) or for a specific server. Supports a minimum rounds filter to exclude low-sample-size players. Use this for questions like 'top players by KD', 'best KD ratio with minimum X games', 'highest KD players'.")]
    public async Task<string> GetTopPlayersByKDRatioAsync(
        [Description("Minimum number of rounds/games played to qualify (default: 20)")] int minRounds = 20,
        [Description("Number of players to return (default: 10)")] int limit = 10,
        [Description("Number of days to look back (default: 90)")] int days = 90,
        [Description("Optional server name to filter by a specific server. Leave empty for global leaderboard.")] string? serverName = null)
    {
        logger.LogDebug("AI requesting top KD ratio players, minRounds: {MinRounds}, limit: {Limit}, days: {Days}, server: {Server}",
            minRounds, limit, days, serverName);

        try
        {
            var startDate = DateTime.UtcNow.AddDays(-days);
            var (startYear, startWeek) = GetIsoWeek(startDate);
            var (endYear, endWeek) = GetIsoWeek(DateTime.UtcNow);

            var query = dbContext.PlayerServerStats
                .AsNoTracking()
                .Where(pss =>
                    (pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                    (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek)));

            // Filter by server if specified
            if (!string.IsNullOrEmpty(serverName))
            {
                var serverGuid = await dbContext.Servers
                    .AsNoTracking()
                    .Where(s => s.Name == serverName)
                    .Select(s => s.Guid)
                    .FirstOrDefaultAsync();

                if (serverGuid == null)
                    return $"Server '{serverName}' not found.";

                query = query.Where(pss => pss.ServerGuid == serverGuid);
            }

            var data = await query
                .GroupBy(pss => pss.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalKills = g.Sum(pss => pss.TotalKills),
                    TotalDeaths = g.Sum(pss => pss.TotalDeaths),
                    TotalRounds = g.Sum(pss => pss.TotalRounds)
                })
                .Where(x => x.TotalRounds >= minRounds && (x.TotalKills > 0 || x.TotalDeaths > 0))
                .ToListAsync();

            var result = data
                .Select(x => new
                {
                    x.PlayerName,
                    KDRatio = x.TotalDeaths > 0
                        ? Math.Round((double)x.TotalKills / x.TotalDeaths, 3)
                        : (double)x.TotalKills,
                    x.TotalKills,
                    x.TotalDeaths,
                    x.TotalRounds
                })
                .OrderByDescending(x => x.KDRatio)
                .Take(limit)
                .ToList();

            return JsonSerializer.Serialize(new
            {
                MinRoundsFilter = minRounds,
                DaysAnalyzed = days,
                ServerFilter = serverName ?? "All servers",
                Players = result
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get top KD ratio players");
            return "Could not retrieve top K/D ratio players.";
        }
    }

    [KernelFunction("GetTopPlayersByKillRate")]
    [Description("Gets the top players ranked by kills per minute (kill rate) across ALL servers (global leaderboard) or for a specific server. Supports a minimum rounds filter to exclude low-sample-size players. Use this for questions like 'top kill rate', 'highest kills per minute with minimum X games', 'best kill rate players'.")]
    public async Task<string> GetTopPlayersByKillRateAsync(
        [Description("Minimum number of rounds/games played to qualify (default: 20)")] int minRounds = 20,
        [Description("Number of players to return (default: 10)")] int limit = 10,
        [Description("Number of days to look back (default: 90)")] int days = 90,
        [Description("Optional server name to filter by a specific server. Leave empty for global leaderboard.")] string? serverName = null)
    {
        logger.LogDebug("AI requesting top kill rate players, minRounds: {MinRounds}, limit: {Limit}, days: {Days}, server: {Server}",
            minRounds, limit, days, serverName);

        try
        {
            var startDate = DateTime.UtcNow.AddDays(-days);
            var (startYear, startWeek) = GetIsoWeek(startDate);
            var (endYear, endWeek) = GetIsoWeek(DateTime.UtcNow);

            var query = dbContext.PlayerServerStats
                .AsNoTracking()
                .Where(pss =>
                    (pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                    (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek)));

            // Filter by server if specified
            if (!string.IsNullOrEmpty(serverName))
            {
                var serverGuid = await dbContext.Servers
                    .AsNoTracking()
                    .Where(s => s.Name == serverName)
                    .Select(s => s.Guid)
                    .FirstOrDefaultAsync();

                if (serverGuid == null)
                    return $"Server '{serverName}' not found.";

                query = query.Where(pss => pss.ServerGuid == serverGuid);
            }

            var data = await query
                .GroupBy(pss => pss.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalKills = g.Sum(pss => pss.TotalKills),
                    TotalDeaths = g.Sum(pss => pss.TotalDeaths),
                    TotalPlayTimeMinutes = g.Sum(pss => pss.TotalPlayTimeMinutes),
                    TotalRounds = g.Sum(pss => pss.TotalRounds)
                })
                .Where(x => x.TotalRounds >= minRounds && x.TotalKills > 0 && x.TotalPlayTimeMinutes > 0)
                .ToListAsync();

            var result = data
                .Select(x => new
                {
                    x.PlayerName,
                    KillsPerMinute = x.TotalPlayTimeMinutes > 0
                        ? Math.Round(x.TotalKills / x.TotalPlayTimeMinutes, 3)
                        : 0.0,
                    x.TotalKills,
                    x.TotalDeaths,
                    PlayTimeHours = Math.Round(x.TotalPlayTimeMinutes / 60.0, 1),
                    x.TotalRounds
                })
                .OrderByDescending(x => x.KillsPerMinute)
                .Take(limit)
                .ToList();

            return JsonSerializer.Serialize(new
            {
                MinRoundsFilter = minRounds,
                DaysAnalyzed = days,
                ServerFilter = serverName ?? "All servers",
                Players = result
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get top kill rate players");
            return "Could not retrieve top kill rate players.";
        }
    }

    private static (int Year, int Week) GetIsoWeek(DateTime date)
    {
        var week = System.Globalization.ISOWeek.GetWeekOfYear(date);
        var year = System.Globalization.ISOWeek.GetYear(date);
        return (year, week);
    }

    [KernelFunction("SearchPlayers")]
    [Description("Search for players by partial name. Returns up to 10 matching players with basic stats.")]
    public async Task<string> SearchPlayersAsync(
        [Description("The partial player name to search for (minimum 3 characters)")] string query,
        [Description("Game to search: 'bf1942', 'fh2', or 'bfvietnam'")] string game = "bf1942")
    {
        logger.LogDebug("AI searching players with query: {Query}, game: {Game}", query, game);

        if (query.Length < 3)
        {
            return "Search query must be at least 3 characters long.";
        }

        var result = await dataExplorerService.SearchPlayersAsync(query, game);
        if (result.Players.Count == 0)
        {
            return $"No players found matching '{query}' in {game}.";
        }

        var players = result.Players.Take(10).Select(p => new
        {
            p.PlayerName,
            p.TotalScore,
            p.TotalKills,
            p.TotalDeaths,
            p.KdRatio,
            p.TotalRounds,
            p.UniqueMaps,
            p.UniqueServers
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            MatchCount = result.Players.Count,
            TopMatches = players
        });
    }
}
