using System.Diagnostics;
using System.Globalization;
using api.Analytics.Models;
using api.Players.Models;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace api.PlayerStats;

/// <summary>
/// SQLite-based player comparison service that queries pre-computed tables.
/// </summary>
public class SqlitePlayerComparisonService(
    PlayerTrackerDbContext dbContext,
    ISqlitePlayerStatsService playerStatsService) : ISqlitePlayerComparisonService
{
    /// <inheritdoc/>
    public async Task<PlayerComparisonResult> ComparePlayersAsync(
        string player1, string player2, string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("ComparePlayersAsync");
        activity?.SetTag("query.name", "ComparePlayers");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        var result = new PlayerComparisonResult
        {
            Player1 = player1,
            Player2 = player2
        };

        // 1. Kill Rates
        result.KillRates = await GetKillRatesAsync(player1, player2, serverGuid);

        // 2. Totals in Buckets
        result.BucketTotals = await GetBucketTotalsAsync(player1, player2, serverGuid);

        // 3. Average Ping
        var pingData = await playerStatsService.GetAveragePingAsync([player1, player2]);
        result.AveragePing = pingData.Select(p => new PingComparison
        {
            PlayerName = p.Key,
            AveragePing = p.Value
        }).ToList();

        // 4. Map Performance
        result.MapPerformance = await GetMapPerformanceAsync(player1, player2, serverGuid);

        // 5. Head-to-Head sessions
        var (headToHeadSessions, headToHeadServerGuids) = await GetHeadToHeadDataAsync(player1, player2, serverGuid);

        // Convert server GUIDs to names
        var allServerGuids = new HashSet<string>(headToHeadServerGuids);
        if (!string.IsNullOrEmpty(serverGuid))
            allServerGuids.Add(serverGuid);

        var serverNames = await dbContext.Servers
            .Where(s => allServerGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        result.HeadToHead = headToHeadSessions.Select(h =>
        {
            h.ServerName = serverNames.GetValueOrDefault(h.ServerName, h.ServerName);
            return h;
        }).ToList();

        // 6. Hourly overlap (last 30 days)
        result.HourlyOverlap = await GetHourlyOverlapAsync(player1, player2, serverGuid);

        // 6. Common Servers
        var (commonServers, _) = await GetCommonServersDataAsync(player1, player2);
        result.CommonServers = commonServers;

        // 7. Kill Milestones - temporarily disabled pending redesign
        // The PlayerMilestones table has been removed as it only supported a limited set
        // of milestones. Player1KillMilestones and Player2KillMilestones use default empty lists.

        // 8. Server details if serverGuid provided
        if (!string.IsNullOrEmpty(serverGuid))
        {
            result.ServerDetails = await dbContext.Servers
                .Where(s => s.Guid == serverGuid)
                .Select(s => new ServerDetails
                {
                    Guid = s.Guid,
                    Name = s.Name,
                    Ip = s.Ip ?? "",
                    Port = s.Port,
                    GameId = s.GameId ?? "",
                    Country = s.Country,
                    Region = s.Region,
                    City = s.City,
                    Timezone = s.Timezone,
                    Org = s.Org
                })
                .FirstOrDefaultAsync();
        }

        stopwatch.Stop();
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "Multiple");

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<BucketTotalsComparison>> GetBucketTotalsAsync(
        string player1, string player2, string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetBucketTotalsAsync");
        activity?.SetTag("query.name", "GetBucketTotals");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        var results = new List<BucketTotalsComparison>();
        var playerNames = new[] { player1, player2 };

        // AllTime: Use PlayerServerStats if server filter specified, otherwise PlayerStatsMonthly
        var allTimeBucket = new BucketTotalsComparison { Bucket = "AllTime" };

        if (!string.IsNullOrEmpty(serverGuid))
        {
            // Server-specific all-time stats
            var serverStats = await dbContext.PlayerServerStats
                .Where(p => playerNames.Contains(p.PlayerName) && p.ServerGuid == serverGuid)
                .ToListAsync();

            foreach (var stats in serverStats)
            {
                var totals = new PlayerTotals
                {
                    Score = stats.TotalScore,
                    Kills = (uint)stats.TotalKills,
                    Deaths = (uint)stats.TotalDeaths,
                    PlayTimeMinutes = stats.TotalPlayTimeMinutes
                };

                if (stats.PlayerName == player1)
                    allTimeBucket.Player1Totals = totals;
                else if (stats.PlayerName == player2)
                    allTimeBucket.Player2Totals = totals;
            }
        }
        else
        {
            // Global all-time stats - SUM across all months
            var lifetimeStats = await dbContext.PlayerStatsMonthly
                .Where(p => playerNames.Contains(p.PlayerName))
                .GroupBy(p => p.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalScore = g.Sum(p => p.TotalScore),
                    TotalKills = g.Sum(p => p.TotalKills),
                    TotalDeaths = g.Sum(p => p.TotalDeaths),
                    TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
                })
                .ToListAsync();

            foreach (var stats in lifetimeStats)
            {
                var totals = new PlayerTotals
                {
                    Score = stats.TotalScore,
                    Kills = (uint)stats.TotalKills,
                    Deaths = (uint)stats.TotalDeaths,
                    PlayTimeMinutes = stats.TotalPlayTimeMinutes
                };

                if (stats.PlayerName == player1)
                    allTimeBucket.Player1Totals = totals;
                else if (stats.PlayerName == player2)
                    allTimeBucket.Player2Totals = totals;
            }
        }
        results.Add(allTimeBucket);

        // Rolling periods: Compute from PlayerServerStats (aggregated across all servers)
        var today = DateTime.UtcNow;
        var rollingPeriods = new[]
        {
            ("Last30Days", today.AddDays(-30)),
            ("Last6Months", today.AddMonths(-6)),
            ("LastYear", today.AddYears(-1))
        };

        foreach (var (label, cutoffDate) in rollingPeriods)
        {
            var (cutoffYear, cutoffWeek) = GetIsoWeek(cutoffDate);
            var (nowYear, nowWeek) = GetIsoWeek(today);

            // Query PlayerServerStats with date range filter
            var query = dbContext.PlayerServerStats
                .Where(p => playerNames.Contains(p.PlayerName))
                .Where(p => (p.Year > cutoffYear) ||
                           (p.Year == cutoffYear && p.Week >= cutoffWeek));

            if (!string.IsNullOrEmpty(serverGuid))
            {
                query = query.Where(p => p.ServerGuid == serverGuid);
            }

            // Group by player (aggregating across all servers when no server filter)
            var aggregatedStats = await query
                .GroupBy(p => p.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalScore = g.Sum(p => p.TotalScore),
                    TotalKills = g.Sum(p => p.TotalKills),
                    TotalDeaths = g.Sum(p => p.TotalDeaths),
                    TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
                })
                .ToListAsync();

            var bucket = new BucketTotalsComparison { Bucket = label };

            var player1Stats = aggregatedStats.FirstOrDefault(s => s.PlayerName == player1);
            var player2Stats = aggregatedStats.FirstOrDefault(s => s.PlayerName == player2);

            bucket.Player1Totals = player1Stats is not null
                ? new PlayerTotals
                {
                    Score = player1Stats.TotalScore,
                    Kills = (uint)player1Stats.TotalKills,
                    Deaths = (uint)player1Stats.TotalDeaths,
                    PlayTimeMinutes = player1Stats.TotalPlayTimeMinutes
                }
                : new PlayerTotals();

            bucket.Player2Totals = player2Stats is not null
                ? new PlayerTotals
                {
                    Score = player2Stats.TotalScore,
                    Kills = (uint)player2Stats.TotalKills,
                    Deaths = (uint)player2Stats.TotalDeaths,
                    PlayTimeMinutes = player2Stats.TotalPlayTimeMinutes
                }
                : new PlayerTotals();

            results.Add(bucket);
        }

        stopwatch.Stop();
        activity?.SetTag("result.row_count", results.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerStatsMonthly,PlayerServerStats");

        return results;
    }

    /// <inheritdoc/>
    public async Task<List<MapPerformanceComparison>> GetMapPerformanceAsync(
        string player1, string player2, string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetMapPerformanceAsync");
        activity?.SetTag("query.name", "GetMapPerformance");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        var playerNames = new[] { player1, player2 };

        var query = dbContext.PlayerMapStats
            .Where(p => playerNames.Contains(p.PlayerName));

        if (!string.IsNullOrEmpty(serverGuid))
        {
            query = query.Where(p => p.ServerGuid == serverGuid);
        }

        var mapStats = await query.ToListAsync();

        // Group by map and create comparison entries
        var mapGroups = mapStats.GroupBy(s => s.MapName);
        var results = new List<MapPerformanceComparison>();

        foreach (var group in mapGroups)
        {
            var comparison = new MapPerformanceComparison { MapName = group.Key };

            var player1MapStats = group.Where(s => s.PlayerName == player1).ToList();
            var player2MapStats = group.Where(s => s.PlayerName == player2).ToList();

            comparison.Player1Totals = new PlayerTotals
            {
                Score = player1MapStats.Sum(s => s.TotalScore),
                Kills = (uint)player1MapStats.Sum(s => s.TotalKills),
                Deaths = (uint)player1MapStats.Sum(s => s.TotalDeaths),
                PlayTimeMinutes = player1MapStats.Sum(s => s.TotalPlayTimeMinutes)
            };

            comparison.Player2Totals = new PlayerTotals
            {
                Score = player2MapStats.Sum(s => s.TotalScore),
                Kills = (uint)player2MapStats.Sum(s => s.TotalKills),
                Deaths = (uint)player2MapStats.Sum(s => s.TotalDeaths),
                PlayTimeMinutes = player2MapStats.Sum(s => s.TotalPlayTimeMinutes)
            };

            results.Add(comparison);
        }

        stopwatch.Stop();
        activity?.SetTag("result.row_count", results.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerMapStats");

        return results;
    }

    /// <inheritdoc/>
    public async Task<(List<HeadToHeadSession> Sessions, HashSet<string> ServerGuids)> GetHeadToHeadDataAsync(
        string player1, string player2, string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetHeadToHeadDataAsync");
        activity?.SetTag("query.name", "GetHeadToHeadData");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        // Find overlapping rounds using PlayerSessions
        // We need sessions where both players were in the same round
        var player1Query = dbContext.PlayerSessions
            .Where(ps => ps.Player.Name == player1 && !string.IsNullOrEmpty(ps.RoundId));

        var player2Query = dbContext.PlayerSessions
            .Where(ps => ps.Player.Name == player2 && !string.IsNullOrEmpty(ps.RoundId));

        if (!string.IsNullOrEmpty(serverGuid))
        {
            player1Query = player1Query.Where(ps => ps.ServerGuid == serverGuid);
            player2Query = player2Query.Where(ps => ps.ServerGuid == serverGuid);
        }

        // Get both players' sessions
        var player1Sessions = await player1Query
            .OrderByDescending(ps => ps.StartTime)
            .Take(500) // Limit for performance
            .ToListAsync();

        var player2Sessions = await player2Query
            .OrderByDescending(ps => ps.StartTime)
            .Take(500)
            .ToListAsync();

        // Find matching rounds
        var player2RoundMap = player2Sessions
            .Where(s => s.RoundId != null)
            .GroupBy(s => s.RoundId!)
            .ToDictionary(g => g.Key, g => g.First());

        var sessions = new List<HeadToHeadSession>();
        var serverGuids = new HashSet<string>();

        foreach (var p1Session in player1Sessions.Where(s => s.RoundId != null))
        {
            if (player2RoundMap.TryGetValue(p1Session.RoundId!, out var p2Session))
            {
                serverGuids.Add(p1Session.ServerGuid);

                sessions.Add(new HeadToHeadSession
                {
                    Timestamp = p1Session.StartTime,
                    ServerName = p1Session.ServerGuid, // Will be resolved to name later
                    MapName = p1Session.MapName,
                    Player1Score = p1Session.TotalScore,
                    Player1Kills = p1Session.TotalKills,
                    Player1Deaths = p1Session.TotalDeaths,
                    Player2Score = p2Session.TotalScore,
                    Player2Kills = p2Session.TotalKills,
                    Player2Deaths = p2Session.TotalDeaths,
                    Player2Timestamp = p2Session.StartTime,
                    RoundId = p1Session.RoundId
                });

                if (sessions.Count >= 50)
                    break;
            }
        }

        stopwatch.Stop();
        activity?.SetTag("result.row_count", sessions.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerSessions");

        return (sessions, serverGuids);
    }

    /// <inheritdoc/>
    public async Task<(List<ServerDetails> Servers, HashSet<string> ServerGuids)> GetCommonServersDataAsync(
        string player1, string player2)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetCommonServersDataAsync");
        activity?.SetTag("query.name", "GetCommonServersData");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2}");

        var stopwatch = Stopwatch.StartNew();

        // Get servers where both players have played (using PlayerServerStats for efficiency)
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

        var player1Servers = await dbContext.PlayerServerStats
            .Where(p => p.PlayerName == player1)
            .Select(p => p.ServerGuid)
            .Distinct()
            .ToListAsync();

        var player2Servers = await dbContext.PlayerServerStats
            .Where(p => p.PlayerName == player2)
            .Select(p => p.ServerGuid)
            .Distinct()
            .ToListAsync();

        var commonServerGuids = player1Servers.Intersect(player2Servers).ToHashSet();

        // Get server details
        var serverDetails = await dbContext.Servers
            .Where(s => commonServerGuids.Contains(s.Guid))
            .Select(s => new ServerDetails
            {
                Guid = s.Guid,
                Name = s.Name,
                Ip = s.Ip ?? "",
                Port = s.Port,
                GameId = s.GameId ?? "",
                Country = s.Country,
                Region = s.Region,
                City = s.City,
                Timezone = s.Timezone,
                Org = s.Org
            })
            .ToListAsync();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", serverDetails.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats,Servers");

        return (serverDetails, commonServerGuids);
    }

    /// <summary>
    /// Gets kill rates (kills per minute) for two players.
    /// </summary>
    private async Task<List<KillRateComparison>> GetKillRatesAsync(
        string player1, string player2, string? serverGuid = null)
    {
        var playerNames = new[] { player1, player2 };

        if (!string.IsNullOrEmpty(serverGuid))
        {
            // Server-specific kill rates from PlayerServerStats
            var serverStats = await dbContext.PlayerServerStats
                .Where(p => playerNames.Contains(p.PlayerName) && p.ServerGuid == serverGuid)
                .GroupBy(p => p.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalKills = g.Sum(p => p.TotalKills),
                    TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
                })
                .ToListAsync();

            return serverStats.Select(s => new KillRateComparison
            {
                PlayerName = s.PlayerName,
                KillRate = s.TotalPlayTimeMinutes > 0 ? s.TotalKills / s.TotalPlayTimeMinutes : 0
            }).ToList();
        }
        else
        {
            // Global kill rates from PlayerStatsMonthly
            var lifetimeStats = await dbContext.PlayerStatsMonthly
                .Where(p => playerNames.Contains(p.PlayerName))
                .GroupBy(p => p.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    TotalKills = g.Sum(p => p.TotalKills),
                    TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
                })
                .ToListAsync();

            return lifetimeStats.Select(s => new KillRateComparison
            {
                PlayerName = s.PlayerName,
                KillRate = s.TotalPlayTimeMinutes > 0 ? s.TotalKills / s.TotalPlayTimeMinutes : 0
            }).ToList();
        }
    }

    private async Task<List<HourlyOverlap>> GetHourlyOverlapAsync(
        string player1, string player2, string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetHourlyOverlapAsync");
        activity?.SetTag("query.name", "GetHourlyOverlap");
        activity?.SetTag("query.filters", $"player1:{player1},player2:{player2},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-30);

        var player1Intervals = await GetPlayerIntervalsAsync(player1, startPeriod, endPeriod, serverGuid);
        var player2Intervals = await GetPlayerIntervalsAsync(player2, startPeriod, endPeriod, serverGuid);

        var player1MinutesByHour = InitializeHourlyBuckets();
        var player2MinutesByHour = InitializeHourlyBuckets();
        var overlapMinutesByHour = InitializeHourlyBuckets();

        foreach (var interval in player1Intervals)
        {
            AccumulateMinutesByHour(player1MinutesByHour, interval.Start, interval.End);
        }

        foreach (var interval in player2Intervals)
        {
            AccumulateMinutesByHour(player2MinutesByHour, interval.Start, interval.End);
        }

        var overlapIntervals = GetOverlapIntervals(player1Intervals, player2Intervals);
        foreach (var interval in overlapIntervals)
        {
            AccumulateMinutesByHour(overlapMinutesByHour, interval.Start, interval.End);
        }

        var results = Enumerable.Range(0, 24)
            .Select(hour => new HourlyOverlap
            {
                Hour = hour,
                Player1Minutes = player1MinutesByHour[hour],
                Player2Minutes = player2MinutesByHour[hour],
                OverlapMinutes = overlapMinutesByHour[hour]
            })
            .ToList();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", results.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerSessions");

        return results;
    }

    private async Task<List<(DateTime Start, DateTime End)>> GetPlayerIntervalsAsync(
        string playerName,
        DateTime startPeriod,
        DateTime endPeriod,
        string? serverGuid)
    {
        var query = dbContext.PlayerSessions
            .AsNoTracking()
            .Where(ps => ps.PlayerName == playerName)
            .Where(ps => ps.StartTime <= endPeriod && ps.LastSeenTime >= startPeriod);

        if (!string.IsNullOrEmpty(serverGuid))
        {
            query = query.Where(ps => ps.ServerGuid == serverGuid);
        }

        var sessions = await query
            .Select(ps => new { ps.StartTime, ps.LastSeenTime })
            .ToListAsync();

        var intervals = sessions
            .Select(session => (
                Start: session.StartTime < startPeriod ? startPeriod : session.StartTime,
                End: session.LastSeenTime > endPeriod ? endPeriod : session.LastSeenTime))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ToList();

        return MergeIntervals(intervals);
    }

    private static List<(DateTime Start, DateTime End)> MergeIntervals(
        List<(DateTime Start, DateTime End)> intervals)
    {
        if (intervals.Count == 0)
            return intervals;

        var merged = new List<(DateTime Start, DateTime End)> { intervals[0] };

        foreach (var interval in intervals.Skip(1))
        {
            var last = merged[^1];
            if (interval.Start <= last.End)
            {
                merged[^1] = (last.Start, interval.End > last.End ? interval.End : last.End);
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    private static List<(DateTime Start, DateTime End)> GetOverlapIntervals(
        List<(DateTime Start, DateTime End)> player1Intervals,
        List<(DateTime Start, DateTime End)> player2Intervals)
    {
        var overlaps = new List<(DateTime Start, DateTime End)>();
        var i = 0;
        var j = 0;

        while (i < player1Intervals.Count && j < player2Intervals.Count)
        {
            var a = player1Intervals[i];
            var b = player2Intervals[j];

            var start = a.Start > b.Start ? a.Start : b.Start;
            var end = a.End < b.End ? a.End : b.End;

            if (end > start)
            {
                overlaps.Add((start, end));
            }

            if (a.End < b.End)
                i++;
            else
                j++;
        }

        return overlaps;
    }

    private static Dictionary<int, double> InitializeHourlyBuckets()
    {
        return Enumerable.Range(0, 24).ToDictionary(hour => hour, _ => 0.0);
    }

    private static void AccumulateMinutesByHour(
        Dictionary<int, double> minutesByHour,
        DateTime start,
        DateTime end)
    {
        var currentTime = start;

        while (currentTime < end)
        {
            var hour = currentTime.Hour;
            var hourEnd = new DateTime(
                currentTime.Year,
                currentTime.Month,
                currentTime.Day,
                hour,
                59,
                59,
                999,
                currentTime.Kind);

            if (hourEnd > end)
            {
                hourEnd = end;
            }

            var minutesInHour = (hourEnd - currentTime).TotalMinutes;
            if (minutesInHour > 0)
            {
                minutesByHour[hour] += minutesInHour;
            }

            currentTime = hourEnd.AddMilliseconds(1);
        }
    }

    /// <summary>
    /// Gets the ISO week number for a given date.
    /// ISO weeks start on Monday and the first week contains January 4th.
    /// </summary>
    private static (int Year, int Week) GetIsoWeek(DateTime date)
    {
        var week = ISOWeek.GetWeekOfYear(date);
        var year = ISOWeek.GetYear(date);
        return (year, week);
    }
}
