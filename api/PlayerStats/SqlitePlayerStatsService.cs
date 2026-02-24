using System.Diagnostics;
using System.Globalization;
using api.Analytics.Models;
using api.Players.Models;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using ServerStatistics = api.Analytics.Models.ServerStatistics;

namespace api.PlayerStats;

/// <summary>
/// SQLite-based player stats service that queries pre-computed tables.
/// </summary>
public class SqlitePlayerStatsService(PlayerTrackerDbContext dbContext) : ISqlitePlayerStatsService
{
    private sealed class HighestScoreSession
    {
        public string ServerGuid { get; set; } = "";
        public int HighestScore { get; set; }
        public string? HighestScoreRoundId { get; set; }
    }

    private async Task<List<HighestScoreSession>> GetHighestScoreSessionsAsync(
        string playerName,
        string[] serverGuids)
    {
        if (serverGuids.Length == 0)
            return [];

        var serverGuidsIn = string.Join(",", serverGuids.Select(g => $"'{g.Replace("'", "''")}'"));

        var sql = $"""
            SELECT ServerGuid, TotalScore AS HighestScore, RoundId AS HighestScoreRoundId
            FROM (
                SELECT 
                    ServerGuid, 
                    TotalScore, 
                    RoundId,
                    ROW_NUMBER() OVER (
                        PARTITION BY ServerGuid 
                        ORDER BY TotalScore DESC, StartTime DESC
                    ) AS RowNum
                FROM PlayerSessions
                WHERE PlayerName = @playerName 
                  AND ServerGuid IN ({serverGuidsIn})
            )
            WHERE RowNum = 1
            """;

        return await dbContext.Database
            .SqlQueryRaw<HighestScoreSession>(sql,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", playerName))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<PlayerLifetimeStats?> GetPlayerStatsAsync(string playerName, int lookBackDays = 30)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetPlayerStatsAsync");
        activity?.SetTag("query.name", "GetPlayerStats");
        activity?.SetTag("query.filters", $"player:{playerName},lookBackDays:{lookBackDays}");

        var stopwatch = Stopwatch.StartNew();

        var query = dbContext.PlayerStatsMonthly.Where(p => p.PlayerName == playerName);
        if (lookBackDays > 0)
        {
            var cutoff = Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-lookBackDays), DateTimeKind.Utc));
            query = query.Where(p => p.LastRoundTime >= cutoff);
        }

        // SUM across matching months for lifetime stats
        var stats = await query
            .GroupBy(p => p.PlayerName)
            .Select(g => new
            {
                PlayerName = g.Key,
                TotalRounds = g.Sum(p => p.TotalRounds),
                TotalKills = g.Sum(p => p.TotalKills),
                TotalDeaths = g.Sum(p => p.TotalDeaths),
                TotalScore = g.Sum(p => p.TotalScore),
                TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes),
                FirstRoundTime = g.Min(p => p.FirstRoundTime),
                LastRoundTime = g.Max(p => p.LastRoundTime)
            })
            .SingleOrDefaultAsync();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", stats != null ? 1 : 0);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerStatsMonthly");

        if (stats == null)
            return null;

        // Calculate derived stats from totals
        var avgScorePerRound = stats.TotalRounds > 0 ? (double)stats.TotalScore / stats.TotalRounds : 0;
        var kdRatio = stats.TotalDeaths > 0 ? (double)stats.TotalKills / stats.TotalDeaths : stats.TotalKills;
        var killRate = stats.TotalPlayTimeMinutes > 0 ? stats.TotalKills / stats.TotalPlayTimeMinutes : 0;

        return new PlayerLifetimeStats
        {
            PlayerName = stats.PlayerName,
            TotalRounds = stats.TotalRounds,
            TotalKills = stats.TotalKills,
            TotalDeaths = stats.TotalDeaths,
            TotalScore = stats.TotalScore,
            TotalPlayTimeMinutes = stats.TotalPlayTimeMinutes,
            AvgScorePerRound = avgScorePerRound,
            KdRatio = kdRatio,
            KillRate = killRate,
            FirstRoundTime = stats.FirstRoundTime.ToDateTimeUtc(),
            LastRoundTime = stats.LastRoundTime.ToDateTimeUtc()
        };
    }

    /// <inheritdoc/>
    public async Task<List<ServerStatistics>> GetPlayerMapStatsAsync(
        string playerName,
        TimePeriod period,
        string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetPlayerMapStatsAsync");
        activity?.SetTag("query.name", "GetPlayerMapStats");
        activity?.SetTag("query.filters", $"player:{playerName},server:{serverGuid ?? "all"},period:{period}");

        var stopwatch = Stopwatch.StartNew();

        // Filter by server or use global stats (empty string)
        var targetServerGuid = string.IsNullOrEmpty(serverGuid)
            ? Data.Entities.PlayerMapStats.GlobalServerGuid
            : serverGuid;

        var now = DateTime.UtcNow;
        var (startYear, startMonth, endYear, endMonth) = period switch
        {
            TimePeriod.ThisYear => (now.Year, 1, now.Year, 12),
            TimePeriod.LastYear => (now.Year - 1, 1, now.Year - 1, 12),
            TimePeriod.Last30Days => (now.AddDays(-30).Year, now.AddDays(-30).Month, now.Year, now.Month),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unsupported time period")
        };

        // SUM across monthly buckets within the requested period
        var mapStats = await dbContext.PlayerMapStats
            .Where(p => p.PlayerName == playerName && p.ServerGuid == targetServerGuid)
            .Where(p =>
                (p.Year > startYear || (p.Year == startYear && p.Month >= startMonth)) &&
                (p.Year < endYear || (p.Year == endYear && p.Month <= endMonth)))
            .GroupBy(p => p.MapName)
            .Select(g => new
            {
                MapName = g.Key,
                TotalScore = g.Sum(p => p.TotalScore),
                TotalKills = g.Sum(p => p.TotalKills),
                TotalDeaths = g.Sum(p => p.TotalDeaths),
                TotalRounds = g.Sum(p => p.TotalRounds),
                TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
            })
            .OrderByDescending(p => p.TotalKills)
            .ToListAsync();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", mapStats.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerMapStats");

        return mapStats.Select(m => new ServerStatistics
        {
            MapName = m.MapName,
            TotalScore = m.TotalScore,
            TotalKills = m.TotalKills,
            TotalDeaths = m.TotalDeaths,
            SessionsPlayed = m.TotalRounds,
            TotalPlayTimeMinutes = (int)m.TotalPlayTimeMinutes
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<ServerInsight>> GetPlayerServerInsightsAsync(string playerName, int lookBackDays = 30)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetPlayerServerInsightsAsync");
        activity?.SetTag("query.name", "GetPlayerServerInsights");
        activity?.SetTag("query.filters", $"player:{playerName},lookBackDays:{lookBackDays}");

        var stopwatch = Stopwatch.StartNew();

        var query = dbContext.PlayerServerStats.Where(p => p.PlayerName == playerName);
        if (lookBackDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-lookBackDays);
            var (cutoffYear, cutoffWeek) = GetIsoWeek(cutoff);
            query = query.Where(p =>
                (p.Year > cutoffYear) ||
                (p.Year == cutoffYear && p.Week >= cutoffWeek));
        }

        // SUM across matching weeks, then filter by 10+ hours (600 minutes)
        var serverStats = await query
            .GroupBy(p => p.ServerGuid)
            .Select(g => new
            {
                ServerGuid = g.Key,
                TotalRounds = g.Sum(p => p.TotalRounds),
                TotalKills = g.Sum(p => p.TotalKills),
                TotalDeaths = g.Sum(p => p.TotalDeaths),
                TotalScore = g.Sum(p => p.TotalScore),
                TotalPlayTimeMinutes = g.Sum(p => p.TotalPlayTimeMinutes)
            })
            .Where(s => s.TotalPlayTimeMinutes >= 600)
            .OrderByDescending(s => s.TotalPlayTimeMinutes)
            .ToListAsync();

        // Get server names
        var serverGuids = serverStats.Select(s => s.ServerGuid).ToArray();
        var servers = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => new { s.Name, s.GameId });

        // SQLite doesn't support APPLY operations, so use raw SQL with window functions
        var highestScoreSessions = await GetHighestScoreSessionsAsync(playerName, serverGuids);

        var highestScoresByServer = highestScoreSessions
            .ToDictionary(s => s.ServerGuid, s => s);

        stopwatch.Stop();
        activity?.SetTag("result.row_count", serverStats.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerServerStats");

        return serverStats.Select(s =>
        {
            var killsPerMinute = s.TotalPlayTimeMinutes > 0
                ? Math.Round(s.TotalKills / s.TotalPlayTimeMinutes, 3)
                : 0;

            servers.TryGetValue(s.ServerGuid, out var serverInfo);
            highestScoresByServer.TryGetValue(s.ServerGuid, out var highestScore);

            return new ServerInsight
            {
                ServerGuid = s.ServerGuid,
                ServerName = serverInfo?.Name ?? "Unknown Server",
                GameId = serverInfo?.GameId ?? "",
                TotalMinutes = s.TotalPlayTimeMinutes,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                HighestScore = highestScore?.HighestScore ?? 0,
                HighestScoreRoundId = highestScore?.HighestScoreRoundId ?? "",
                KillsPerMinute = killsPerMinute,
                TotalRounds = s.TotalRounds
            };
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<PlayerBestScores> GetPlayerBestScoresAsync(string playerName, int lookBackDays = 30)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetPlayerBestScoresAsync");
        activity?.SetTag("query.name", "GetPlayerBestScores");
        activity?.SetTag("query.filters", $"player:{playerName},lookBackDays:{lookBackDays}");

        var stopwatch = Stopwatch.StartNew();

        var query = dbContext.PlayerBestScores.Where(p => p.PlayerName == playerName);
        if (lookBackDays > 0)
        {
            var cutoff = Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-lookBackDays), DateTimeKind.Utc));
            query = query.Where(p => p.RoundEndTime >= cutoff);
        }

        var bestScores = await query
            .OrderBy(p => p.Period)
            .ThenBy(p => p.Rank)
            .ToListAsync();

        // Get server names
        var serverGuids = bestScores.Select(s => s.ServerGuid).Distinct().ToArray();
        var serverNames = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        stopwatch.Stop();
        activity?.SetTag("result.row_count", bestScores.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerBestScores");

        var result = new PlayerBestScores();

        foreach (var score in bestScores)
        {
            serverNames.TryGetValue(score.ServerGuid, out var serverName);

            var detail = new BestScoreDetail
            {
                Score = score.FinalScore,
                Kills = score.FinalKills,
                Deaths = score.FinalDeaths,
                MapName = score.MapName,
                ServerName = serverName ?? "Unknown Server",
                ServerGuid = score.ServerGuid,
                Timestamp = score.RoundEndTime.ToDateTimeUtc(),
                RoundId = score.RoundId
            };

            switch (score.Period.ToLowerInvariant())
            {
                case "this_week":
                    result.ThisWeek.Add(detail);
                    break;
                case "last_30_days":
                    result.Last30Days.Add(detail);
                    break;
                case "all_time":
                    result.AllTime.Add(detail);
                    break;
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, double>> GetAveragePingAsync(string[] playerNames, int sampleSize = 50)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetAveragePingAsync");
        activity?.SetTag("query.name", "GetAveragePing");
        activity?.SetTag("query.filters", $"players:{playerNames.Length},sampleSize:{sampleSize}");

        var stopwatch = Stopwatch.StartNew();

        // Get the last N sessions per player and calculate average ping from those
        // This ensures we get a consistent sample size regardless of how long ago they played
        var pingData = new List<dynamic>();

        foreach (var playerName in playerNames)
        {
            var avgPing = await dbContext.PlayerSessions
                .Where(ps => ps.Player.Name == playerName &&
                            ps.AveragePing > 0 &&
                            ps.AveragePing < 1000)
                .OrderByDescending(ps => ps.StartTime)
                .Take(sampleSize)
                .Select(ps => ps.AveragePing)
                .AverageAsync();

            pingData.Add(new
            {
                PlayerName = playerName,
                AvgPing = avgPing
            });
        }

        stopwatch.Stop();
        activity?.SetTag("result.row_count", pingData.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "PlayerSessions");

        return pingData.ToDictionary(p => (string)p.PlayerName, p => (double?)p.AvgPing ?? 0);
    }

    private static (int Year, int Week) GetIsoWeek(DateTime date)
    {
        var week = ISOWeek.GetWeekOfYear(date);
        var year = ISOWeek.GetYear(date);
        return (year, week);
    }
}
