using System.Diagnostics;
using api.PlayerTracking;
using api.Servers.Models;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace api.PlayerStats;

/// <summary>
/// SQLite-based leaderboard service that queries pre-computed weekly aggregates.
/// Aggregates PlayerServerStats records across weeks for the requested time period.
/// </summary>
public class SqliteLeaderboardService(PlayerTrackerDbContext dbContext) : ISqliteLeaderboardService
{
    private const int MinRoundsDefault = 3;

    /// <inheritdoc/>
    public async Task<List<TopScore>> GetTopScoresAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10,
        int? minRoundsOverride = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetTopScoresAsync");
        activity?.SetTag("query.name", "GetTopScores");
        activity?.SetTag("query.filters", $"server:{serverGuid},limit:{limit}");

        var stopwatch = Stopwatch.StartNew();
        var minRounds = minRoundsOverride ?? MinRoundsDefault;

        var (startYear, startWeek) = GetIsoWeek(startPeriod);
        var (endYear, endWeek) = GetIsoWeek(endPeriod);

        var result = await dbContext.PlayerServerStats
            .Where(pss => pss.ServerGuid == serverGuid &&
                         ((pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                          (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek))))
            .GroupBy(pss => pss.PlayerName)
            .Select(g => new
            {
                PlayerName = g.Key,
                TotalScore = g.Sum(pss => pss.TotalScore),
                TotalKills = g.Sum(pss => pss.TotalKills),
                TotalDeaths = g.Sum(pss => pss.TotalDeaths),
                TotalRounds = g.Sum(pss => pss.TotalRounds)
            })
            .Where(x => x.TotalRounds >= minRounds && x.TotalScore > 0)
            .OrderByDescending(x => x.TotalScore)
            .Take(limit)
            .Select(x => new TopScore
            {
                PlayerName = x.PlayerName,
                Score = x.TotalScore,
                Kills = x.TotalKills,
                Deaths = x.TotalDeaths,
                MapName = "",
                Timestamp = DateTime.MinValue,
                SessionId = 0
            })
            .ToListAsync();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", result.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats");

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<TopKDRatio>> GetTopKDRatiosAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10,
        int? minRoundsOverride = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetTopKDRatiosAsync");
        activity?.SetTag("query.name", "GetTopKDRatios");
        activity?.SetTag("query.filters", $"server:{serverGuid},limit:{limit}");

        var stopwatch = Stopwatch.StartNew();
        var minRounds = minRoundsOverride ?? MinRoundsDefault;

        var (startYear, startWeek) = GetIsoWeek(startPeriod);
        var (endYear, endWeek) = GetIsoWeek(endPeriod);

        // Need to fetch and compute K/D ratio in memory since SQLite can't do division in aggregate
        var data = await dbContext.PlayerServerStats
            .Where(pss => pss.ServerGuid == serverGuid &&
                         ((pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                          (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek))))
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
            .Select(x => new TopKDRatio
            {
                PlayerName = x.PlayerName,
                Kills = x.TotalKills,
                Deaths = x.TotalDeaths,
                KDRatio = x.TotalDeaths > 0
                    ? Math.Round((double)x.TotalKills / x.TotalDeaths, 3)
                    : x.TotalKills,
                TotalRounds = x.TotalRounds
            })
            .OrderByDescending(x => x.KDRatio)
            .Take(limit)
            .ToList();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", result.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats");

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<TopKillRate>> GetTopKillRatesAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10,
        int? minRoundsOverride = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetTopKillRatesAsync");
        activity?.SetTag("query.name", "GetTopKillRates");
        activity?.SetTag("query.filters", $"server:{serverGuid},limit:{limit}");

        var stopwatch = Stopwatch.StartNew();
        var minRounds = minRoundsOverride ?? MinRoundsDefault;

        var (startYear, startWeek) = GetIsoWeek(startPeriod);
        var (endYear, endWeek) = GetIsoWeek(endPeriod);

        // Need to fetch and compute kill rate in memory
        var data = await dbContext.PlayerServerStats
            .Where(pss => pss.ServerGuid == serverGuid &&
                         ((pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                          (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek))))
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
            .Select(x => new TopKillRate
            {
                PlayerName = x.PlayerName,
                Kills = x.TotalKills,
                Deaths = x.TotalDeaths,
                PlayTimeMinutes = (int)x.TotalPlayTimeMinutes,
                KillRate = x.TotalPlayTimeMinutes > 0
                    ? Math.Round(x.TotalKills / x.TotalPlayTimeMinutes, 3)
                    : 0,
                TotalRounds = x.TotalRounds
            })
            .OrderByDescending(x => x.KillRate)
            .Take(limit)
            .ToList();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", result.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats");

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<PlayerActivity>> GetMostActivePlayersAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetMostActivePlayersAsync");
        activity?.SetTag("query.name", "GetMostActivePlayers");
        activity?.SetTag("query.filters", $"server:{serverGuid},limit:{limit}");

        var stopwatch = Stopwatch.StartNew();

        var (startYear, startWeek) = GetIsoWeek(startPeriod);
        var (endYear, endWeek) = GetIsoWeek(endPeriod);

        var result = await dbContext.PlayerServerStats
            .Where(pss => pss.ServerGuid == serverGuid &&
                         ((pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                          (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek))))
            .GroupBy(pss => pss.PlayerName)
            .Select(g => new PlayerActivity
            {
                PlayerName = g.Key,
                MinutesPlayed = (int)g.Sum(pss => pss.TotalPlayTimeMinutes),
                TotalKills = g.Sum(pss => pss.TotalKills),
                TotalDeaths = g.Sum(pss => pss.TotalDeaths)
            })
            .Where(x => x.MinutesPlayed > 0)
            .OrderByDescending(x => x.MinutesPlayed)
            .Take(limit)
            .ToListAsync();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", result.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats");

        return result;
    }

    /// <summary>
    /// Gets the ISO week number for a given date.
    /// ISO weeks start on Monday and the first week contains January 4th.
    /// </summary>
    private static (int Year, int Week) GetIsoWeek(DateTime date)
    {
        // Use .NET's ISOWeek helper
        var week = System.Globalization.ISOWeek.GetWeekOfYear(date);
        var year = System.Globalization.ISOWeek.GetYear(date);
        return (year, week);
    }
}
