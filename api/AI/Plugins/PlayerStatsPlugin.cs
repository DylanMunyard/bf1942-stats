using System.ComponentModel;
using System.Text.Json;
using api.Analytics.Models;
using api.DataExplorer;
using api.Players.Models;
using api.PlayerStats;
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
    ILogger<PlayerStatsPlugin> logger)
{
    [KernelFunction("GetPlayerLifetimeStats")]
    [Description("Gets lifetime statistics for a player including total kills, deaths, score, K/D ratio, and playtime.")]
    public async Task<string> GetPlayerLifetimeStatsAsync(
        [Description("The exact player name to look up")] string playerName)
    {
        logger.LogDebug("AI requesting lifetime stats for player: {PlayerName}", playerName);

        var stats = await playerStatsService.GetPlayerStatsAsync(playerName);
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
        [Description("The exact player name to look up")] string playerName)
    {
        logger.LogDebug("AI requesting server insights for player: {PlayerName}", playerName);

        var insights = await playerStatsService.GetPlayerServerInsightsAsync(playerName);
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
        [Description("The exact player name to look up")] string playerName)
    {
        logger.LogDebug("AI requesting best scores for player: {PlayerName}", playerName);

        var bestScores = await playerStatsService.GetPlayerBestScoresAsync(playerName);

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
