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
/// Semantic Kernel plugin for game activity and round analysis.
/// Provides insights about when games happen and activity patterns.
/// </summary>
public class GameActivityPlugin(
    RoundsService roundsService,
    PlayerTrackerDbContext dbContext,
    ILogger<GameActivityPlugin> logger)
{
    [KernelFunction("GetRoundsByGameType")]
    [Description("Gets recent rounds filtered by game type (e.g., CTF, Conquest, TDM) with timing analysis to help users find when specific game modes are played.")]
    public async Task<string> GetRoundsByGameTypeAsync(
        [Description("The game type to filter by (e.g., 'CTF', 'Conquest', 'TDM', 'Coop')")] string gameType,
        [Description("Number of days to look back (default: 30)")] int days = 30)
    {
        logger.LogDebug("AI requesting rounds by game type: {GameType}, days: {Days}", gameType, days);

        var startDate = DateTime.UtcNow.AddDays(-days);
        var filters = new RoundFilters
        {
            GameType = gameType,
            StartTimeFrom = startDate,
            MinParticipants = 2 // At least 2 players
        };

        var result = await roundsService.GetRounds(1, 100, "starttime", "desc", filters);

        if (result.TotalItems == 0)
        {
            return $"No {gameType} rounds found in the last {days} days.";
        }

        // Analyze timing patterns
        var rounds = result.Items;
        var byDayOfWeek = rounds
            .GroupBy(r => r.StartTime.DayOfWeek)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Day = g.Key.ToString(), RoundCount = g.Count() })
            .ToList();

        var byHour = rounds
            .GroupBy(r => r.StartTime.Hour)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { HourUtc = g.Key, RoundCount = g.Count() })
            .ToList();

        var topServers = rounds
            .GroupBy(r => r.ServerName)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => new
            {
                ServerName = g.Key,
                RoundCount = g.Count(),
                AvgPlayers = Math.Round(g.Average(r => r.ParticipantCount), 0)
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            GameType = gameType,
            TotalRoundsInPeriod = result.TotalItems,
            DaysAnalyzed = days,
            MostActiveDay = byDayOfWeek.FirstOrDefault()?.Day,
            ActivityByDayOfWeek = byDayOfWeek,
            PeakHoursUtc = byHour,
            TopServers = topServers
        });
    }

    [KernelFunction("GetHourlyActivityPatterns")]
    [Description("Gets a weekly activity heatmap showing when servers are busiest, broken down by day of week and hour.")]
    public async Task<string> GetHourlyActivityPatternsAsync(
        [Description("Optional server GUID to filter by a specific server")] string? serverGuid = null,
        [Description("Number of days to analyze (default: 30)")] int days = 30)
    {
        logger.LogDebug("AI requesting hourly activity patterns, server: {ServerGuid}, days: {Days}", serverGuid, days);

        var startDate = DateTime.UtcNow.AddDays(-days);

        var query = dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.StartTime >= startDate && r.ParticipantCount > 1);

        if (!string.IsNullOrEmpty(serverGuid))
        {
            query = query.Where(r => r.ServerGuid == serverGuid);
        }

        var rounds = await query
            .Select(r => new { r.StartTime, r.ParticipantCount })
            .ToListAsync();

        if (rounds.Count == 0)
        {
            return "No activity data found for the specified period.";
        }

        // Build activity heatmap by day and hour
        var heatmap = rounds
            .GroupBy(r => new { r.StartTime.DayOfWeek, r.StartTime.Hour })
            .Select(g => new
            {
                Day = g.Key.DayOfWeek.ToString(),
                HourUtc = g.Key.Hour,
                RoundCount = g.Count(),
                AvgPlayers = Math.Round(g.Average(r => (double)(r.ParticipantCount ?? 0)), 1)
            })
            .OrderBy(x => (int)Enum.Parse<DayOfWeek>(x.Day))
            .ThenBy(x => x.HourUtc)
            .ToList();

        // Find peak times
        var peakTimes = heatmap
            .OrderByDescending(x => x.AvgPlayers)
            .Take(5)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            DaysAnalyzed = days,
            TotalRounds = rounds.Count,
            PeakActivityTimes = peakTimes,
            HeatmapSummary = heatmap
                .GroupBy(x => x.Day)
                .Select(g => new
                {
                    Day = g.Key,
                    TotalRounds = g.Sum(x => x.RoundCount),
                    PeakHourUtc = g.OrderByDescending(x => x.AvgPlayers).First().HourUtc
                })
                .ToList()
        });
    }

    [KernelFunction("GetActiveServers")]
    [Description("Gets information about currently active or recently active servers.")]
    public async Task<string> GetActiveServersAsync(
        [Description("How many hours back to consider 'recent' activity (default: 24)")] int hoursBack = 24)
    {
        logger.LogDebug("AI requesting active servers, hours back: {HoursBack}", hoursBack);

        var cutoffTime = DateTime.UtcNow.AddHours(-hoursBack);

        var recentRounds = await dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.StartTime >= cutoffTime)
            .GroupBy(r => new { r.ServerGuid, r.ServerName })
            .Select(g => new
            {
                g.Key.ServerGuid,
                g.Key.ServerName,
                RoundCount = g.Count(),
                LatestRound = g.Max(r => r.StartTime),
                TotalPlayers = g.Sum(r => r.ParticipantCount ?? 0),
                AvgPlayers = g.Average(r => r.ParticipantCount ?? 0),
                IsCurrentlyActive = g.Any(r => r.IsActive)
            })
            .OrderByDescending(s => s.LatestRound)
            .Take(10)
            .ToListAsync();

        if (recentRounds.Count == 0)
        {
            return $"No servers have been active in the last {hoursBack} hours.";
        }

        var result = recentRounds.Select(s => new
        {
            s.ServerName,
            s.RoundCount,
            LastActivity = s.LatestRound.ToString("yyyy-MM-dd HH:mm UTC"),
            AvgPlayersPerRound = Math.Round(s.AvgPlayers, 1),
            CurrentlyActive = s.IsCurrentlyActive
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            HoursAnalyzed = hoursBack,
            ActiveServerCount = recentRounds.Count,
            Servers = result
        });
    }

    [KernelFunction("GetRecentRounds")]
    [Description("Gets recent rounds for a specific server or all servers, optionally filtered by player names.")]
    public async Task<string> GetRecentRoundsAsync(
        [Description("Optional server name to filter by")] string? serverName = null,
        [Description("Optional player name to filter by (shows rounds where this player participated)")] string? playerName = null,
        [Description("Number of rounds to return (default: 10, max: 20)")] int count = 10)
    {
        logger.LogDebug("AI requesting recent rounds, server: {ServerName}, player: {PlayerName}", serverName, playerName);

        count = Math.Min(count, 20);

        var filters = new RoundFilters
        {
            ServerName = serverName,
            PlayerNames = !string.IsNullOrEmpty(playerName) ? [playerName] : null,
            MinParticipants = 2
        };

        var result = await roundsService.GetRounds(1, count, "starttime", "desc", filters, includeTopPlayers: true);

        if (result.TotalItems == 0)
        {
            return "No rounds found matching the criteria.";
        }

        var rounds = result.Items.Select(r => new
        {
            r.ServerName,
            r.MapName,
            r.GameType,
            StartTime = r.StartTime.ToString("yyyy-MM-dd HH:mm UTC"),
            DurationMinutes = r.DurationMinutes,
            PlayerCount = r.ParticipantCount,
            r.IsActive,
            TopPlayers = r.TopPlayers?.Take(3).Select(p => new
            {
                p.PlayerName,
                p.Score,
                p.Kills,
                p.Deaths
            }).ToList()
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            TotalMatchingRounds = result.TotalItems,
            Rounds = rounds
        });
    }
}
