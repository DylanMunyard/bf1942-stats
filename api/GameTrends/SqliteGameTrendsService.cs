using System.Diagnostics;
using api.Bflist.Models;
using api.ClickHouse.Models;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace api.GameTrends;

/// <summary>
/// SQLite-based implementation of game trends service.
/// Queries pre-computed tables instead of ClickHouse for analytics.
/// </summary>
public class SqliteGameTrendsService(PlayerTrackerDbContext dbContext) : ISqliteGameTrendsService
{
    /// <inheritdoc/>
    public async Task<SmartPredictionInsights> GetSmartPredictionInsightsAsync(string? game = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetSmartPredictionInsightsAsync");
        activity?.SetTag("query.name", "GetSmartPredictionInsights");
        activity?.SetTag("query.filters", game ?? "all_games");

        var stopwatch = Stopwatch.StartNew();

        var currentTime = DateTime.UtcNow;
        var currentHour = currentTime.Hour;
        // SQLite/C# uses Sunday=0, Saturday=6
        var currentDayOfWeek = (int)currentTime.DayOfWeek;

        // Get current player count from active sessions (same as ClickHouse service)
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var currentPlayerQuery = dbContext.PlayerSessions
            .Include(ps => ps.Server)
            .Include(ps => ps.Player)
            .Where(ps => ps.IsActive &&
                        ps.LastSeenTime >= oneMinuteAgo &&
                        !ps.Player.AiBot);

        if (!string.IsNullOrEmpty(game))
        {
            currentPlayerQuery = currentPlayerQuery.Where(ps => ps.Server.Game == game);
        }

        var currentActualPlayers = await currentPlayerQuery.CountAsync();

        // Get 8-hour forecast entries (current + next 8 hours)
        var forecastEntries = new List<(int hour, int dayOfWeek)>();
        for (int i = 0; i < 9; i++)
        {
            var futureTime = currentTime.AddHours(i);
            var futureHour = futureTime.Hour;
            var futureDayOfWeek = (int)futureTime.DayOfWeek;
            forecastEntries.Add((futureHour, futureDayOfWeek));
        }

        // Query HourlyPlayerPredictions table
        var predictionsQuery = dbContext.HourlyPlayerPredictions.AsQueryable();

        if (!string.IsNullOrEmpty(game))
        {
            predictionsQuery = predictionsQuery.Where(p => p.Game == game);
        }

        var predictions = await predictionsQuery
            .Where(p => forecastEntries.Select(e => e.dayOfWeek).Contains(p.DayOfWeek) &&
                       forecastEntries.Select(e => e.hour).Contains(p.HourOfDay))
            .ToListAsync();

        // Filter to exact hour/day combinations we need
        var forecast = forecastEntries
            .Select(entry =>
            {
                var prediction = predictions.FirstOrDefault(p =>
                    p.HourOfDay == entry.hour && p.DayOfWeek == entry.dayOfWeek);

                return new HourlyPrediction
                {
                    HourOfDay = entry.hour,
                    DayOfWeek = entry.dayOfWeek,
                    PredictedPlayers = prediction?.PredictedPlayers ?? 0,
                    DataPoints = prediction?.DataPoints ?? 0,
                    IsCurrentHour = entry.hour == currentHour && entry.dayOfWeek == currentDayOfWeek,
                    ActualPlayers = entry.hour == currentHour && entry.dayOfWeek == currentDayOfWeek
                        ? currentActualPlayers : null,
                    Delta = entry.hour == currentHour && entry.dayOfWeek == currentDayOfWeek
                        ? currentActualPlayers - (prediction?.PredictedPlayers ?? 0) : null
                };
            })
            .ToList();

        // Calculate insights
        var currentHourPredicted = forecast.FirstOrDefault(f => f.IsCurrentHour)?.PredictedPlayers ?? 0;

        var nextHourEntry = forecastEntries.ElementAtOrDefault(1);
        var nextHourPredicted = forecast.FirstOrDefault(f =>
            f.HourOfDay == nextHourEntry.hour && f.DayOfWeek == nextHourEntry.dayOfWeek)?.PredictedPlayers ?? 0;

        var maxPredictedPlayers = forecast.Skip(1).Any() ? forecast.Skip(1).Max(f => f.PredictedPlayers) : 0;

        // Determine activity comparison status (guard against division by zero)
        string activityComparison;
        if (currentHourPredicted > 0.01) // Use small threshold to avoid near-zero division
        {
            var ratio = currentActualPlayers / currentHourPredicted;
            activityComparison = ratio switch
            {
                > 1.3 => "busier_than_usual",
                < 0.7 => "quieter_than_usual",
                _ => "as_usual"
            };
        }
        else
        {
            // No prediction data available
            activityComparison = currentActualPlayers > 5 ? "busier_than_usual" : "as_usual";
        }

        var currentStatus = currentActualPlayers switch
        {
            < 5 => "very_quiet",
            < 15 => "quiet",
            < 30 => "moderate",
            < 50 => "busy",
            _ => "very_busy"
        };

        var trendDirection = (nextHourPredicted, currentHourPredicted) switch
        {
            var (next, current) when next > current * 1.2 => "increasing_significantly",
            var (next, current) when next > current * 1.05 => "increasing",
            var (next, current) when next < current * 0.8 => "decreasing_significantly",
            var (next, current) when next < current * 0.95 => "decreasing",
            _ => "stable"
        };

        stopwatch.Stop();
        activity?.SetTag("result.row_count", forecast.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "HourlyPlayerPredictions");

        return new SmartPredictionInsights
        {
            CurrentHourPredictedPlayers = currentHourPredicted,
            CurrentActualPlayers = currentActualPlayers,
            ActivityComparisonStatus = activityComparison,
            CurrentStatus = currentStatus,
            TrendDirection = trendDirection,
            NextHourPredictedPlayers = nextHourPredicted,
            MaxPredictedPlayers = maxPredictedPlayers,
            Forecast = forecast,
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc/>
    public async Task<GroupedServerBusyIndicatorResult> GetServerBusyIndicatorAsync(
        string[] serverGuids,
        int timelineHourRange = 4)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetServerBusyIndicatorAsync");
        activity?.SetTag("query.name", "GetServerBusyIndicator");
        activity?.SetTag("query.filters", $"servers:{serverGuids.Length},range:{timelineHourRange}");

        var stopwatch = Stopwatch.StartNew();

        var currentTime = DateTime.UtcNow;
        var currentHour = currentTime.Hour;
        var currentDayOfWeek = (int)currentTime.DayOfWeek;

        // Get current activity from active sessions
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var currentActivities = await dbContext.PlayerSessions
            .Where(ps => ps.IsActive &&
                        ps.LastSeenTime >= oneMinuteAgo &&
                        !ps.Player.AiBot &&
                        serverGuids.Contains(ps.Server.Guid))
            .GroupBy(ps => ps.Server.Guid)
            .Select(g => new ServerCurrentActivity
            {
                ServerGuid = g.Key,
                CurrentPlayers = g.Count()
            })
            .ToListAsync();

        // Get server info
        var serverInfos = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .Select(s => new GameTrendsServerInfo
            {
                ServerGuid = s.Guid,
                ServerName = s.Name,
                Game = s.GameId
            })
            .ToListAsync();

        // Get hourly patterns from pre-computed table
        var patterns = await dbContext.ServerHourlyPatterns
            .Where(p => serverGuids.Contains(p.ServerGuid) && p.DayOfWeek == currentDayOfWeek)
            .ToListAsync();

        // Build timeline hours
        var timelineHours = new List<int>();
        for (int i = -timelineHourRange; i <= timelineHourRange; i++)
        {
            var hour = (currentHour + i + 24) % 24;
            timelineHours.Add(hour);
        }

        // Build results
        var serverResults = new List<ServerBusyIndicatorResult>();

        foreach (var serverGuid in serverGuids)
        {
            var currentActivity = currentActivities.FirstOrDefault(ca => ca.ServerGuid == serverGuid);
            var serverInfo = serverInfos.FirstOrDefault(si => si.ServerGuid == serverGuid);
            var serverPatterns = patterns.Where(p => p.ServerGuid == serverGuid).ToList();
            var currentHourPattern = serverPatterns.FirstOrDefault(p => p.HourOfDay == currentHour);

            var currentPlayers = currentActivity?.CurrentPlayers ?? 0;

            // Build hourly timeline
            var serverHourlyTimeline = timelineHours.Select(hour =>
            {
                var hourPattern = serverPatterns.FirstOrDefault(p => p.HourOfDay == hour);
                var avgPlayers = hourPattern?.AvgPlayers ?? 0;

                var busyLevel = avgPlayers switch
                {
                    >= 20 => "very_busy",
                    >= 15 => "busy",
                    >= 10 => "moderate",
                    >= 5 => "quiet",
                    _ => "very_quiet"
                };

                return new HourlyBusyData
                {
                    Hour = hour,
                    TypicalPlayers = avgPlayers,
                    BusyLevel = busyLevel,
                    IsCurrentHour = hour == currentHour
                };
            }).ToList();

            BusyIndicatorResult busyIndicator;

            if (currentHourPattern == null || currentHourPattern.DataPoints < 3)
            {
                busyIndicator = new BusyIndicatorResult
                {
                    BusyLevel = "unknown",
                    BusyText = "Not enough data",
                    CurrentPlayers = currentPlayers,
                    TypicalPlayers = 0,
                    Percentile = 0,
                    GeneratedAt = DateTime.UtcNow
                };
            }
            else
            {
                // Use pre-computed percentiles
                string busyLevel;
                string busyText;
                double percentile;

                if (currentPlayers >= currentHourPattern.Q90Players)
                {
                    busyLevel = "very_busy";
                    busyText = "Busier than usual";
                    percentile = 90;
                }
                else if (currentPlayers >= currentHourPattern.Q75Players)
                {
                    busyLevel = "busy";
                    busyText = "Busy";
                    percentile = 75;
                }
                else if (currentPlayers >= currentHourPattern.MedianPlayers)
                {
                    busyLevel = "moderate";
                    busyText = "As busy as usual";
                    percentile = 50;
                }
                else if (currentPlayers >= currentHourPattern.Q25Players)
                {
                    busyLevel = "quiet";
                    busyText = "Not too busy";
                    percentile = 25;
                }
                else
                {
                    busyLevel = "very_quiet";
                    busyText = "Quieter than usual";
                    percentile = 10;
                }

                busyIndicator = new BusyIndicatorResult
                {
                    BusyLevel = busyLevel,
                    BusyText = busyText,
                    CurrentPlayers = currentPlayers,
                    TypicalPlayers = currentHourPattern.AvgPlayers,
                    Percentile = percentile,
                    HistoricalRange = new HistoricalRange
                    {
                        Min = currentHourPattern.MinPlayers,
                        Q25 = currentHourPattern.Q25Players,
                        Median = currentHourPattern.MedianPlayers,
                        Q75 = currentHourPattern.Q75Players,
                        Q90 = currentHourPattern.Q90Players,
                        Max = currentHourPattern.MaxPlayers,
                        Average = currentHourPattern.AvgPlayers
                    },
                    GeneratedAt = DateTime.UtcNow
                };
            }

            serverResults.Add(new ServerBusyIndicatorResult
            {
                ServerGuid = serverGuid,
                ServerName = serverInfo?.ServerName ?? "Unknown Server",
                Game = serverInfo?.Game ?? "Unknown",
                BusyIndicator = busyIndicator,
                HourlyTimeline = serverHourlyTimeline
            });
        }

        stopwatch.Stop();
        activity?.SetTag("result.row_count", serverResults.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "ServerHourlyPatterns");

        return new GroupedServerBusyIndicatorResult
        {
            ServerResults = serverResults,
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc/>
    public async Task<PlayersOnlineHistoryResponse> GetPlayersOnlineHistoryAsync(
        string game,
        string period,
        int rollingWindowDays,
        string? serverGuid = null)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetPlayersOnlineHistoryAsync");
        activity?.SetTag("query.name", "GetPlayersOnlineHistory");
        activity?.SetTag("query.filters", $"game:{game},period:{period},server:{serverGuid ?? "all"}");

        var stopwatch = Stopwatch.StartNew();

        // Parse period
        var (days, intervalMinutes, useAllTime) = period switch
        {
            "1d" => (1, 5, false),
            "3d" => (3, 30, false),
            "7d" => (7, 60, false),
            "1month" or "30d" => (30, 240, false),
            "3months" or "90d" => (90, 720, false),
            "6months" or "180d" => (180, 1440, false),
            "1year" or "365d" => (365, 1440, false),
            "thisyear" => (DateTime.Now.DayOfYear, 1440, false),
            "alltime" => (0, 1440, true),
            _ => ParseCustomDayPeriod(period)
        };

        var query = dbContext.ServerOnlineCounts
            .Where(s => s.Game == game);

        if (!useAllTime)
        {
            var startTime = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-days));
            query = query.Where(s => s.HourTimestamp >= startTime);
        }

        if (!string.IsNullOrEmpty(serverGuid))
        {
            query = query.Where(s => s.ServerGuid == serverGuid);
        }

        // Group by time bucket based on interval
        var rawData = await query
            .OrderBy(s => s.HourTimestamp)
            .ToListAsync();

        // Bucket the data
        var dataPoints = rawData
            .GroupBy(s => GetBucketKey(s.HourTimestamp.ToDateTimeUtc(), intervalMinutes))
            .Select(g => new PlayersOnlineDataPoint
            {
                Timestamp = g.Key,
                TotalPlayers = (int)Math.Round(g.Sum(x => x.AvgPlayers))
            })
            .OrderBy(p => p.Timestamp)
            .ToArray();

        var insights = CalculatePlayerTrendsInsights(dataPoints, period, rollingWindowDays);

        stopwatch.Stop();
        activity?.SetTag("result.row_count", dataPoints.Length);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "ServerOnlineCounts");

        return new PlayersOnlineHistoryResponse
        {
            DataPoints = dataPoints,
            Insights = insights,
            Period = period,
            Game = game,
            LastUpdated = DateTime.UtcNow.ToString("o")
        };
    }

    /// <inheritdoc/>
    public async Task<List<WeeklyActivityPattern>> GetWeeklyActivityPatternsAsync(string? game = null, int daysPeriod = 30)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("GetWeeklyActivityPatternsAsync");
        activity?.SetTag("query.name", "GetWeeklyActivityPatterns");
        activity?.SetTag("query.filters", $"game:{game ?? "all"},days:{daysPeriod}");

        var stopwatch = Stopwatch.StartNew();

        var query = dbContext.HourlyActivityPatterns.AsQueryable();

        if (!string.IsNullOrEmpty(game))
        {
            query = query.Where(p => p.Game == game);
        }

        var patterns = await query
            .OrderBy(p => p.DayOfWeek)
            .ThenBy(p => p.HourOfDay)
            .ToListAsync();

        var result = patterns.Select(p => new WeeklyActivityPattern
        {
            DayOfWeek = p.DayOfWeek,
            HourOfDay = p.HourOfDay,
            UniquePlayers = (int)p.UniquePlayersAvg,
            TotalRounds = (int)p.TotalRoundsAvg,
            AvgRoundDuration = p.AvgRoundDuration,
            PeriodType = p.PeriodType
        }).ToList();

        stopwatch.Stop();
        activity?.SetTag("result.row_count", result.Count);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("result.table", "HourlyActivityPatterns");

        return result;
    }

    private static DateTime GetBucketKey(DateTime timestamp, int intervalMinutes)
    {
        var intervalSeconds = intervalMinutes * 60;
        var unixSeconds = new DateTimeOffset(timestamp).ToUnixTimeSeconds();
        var bucketSeconds = (unixSeconds / intervalSeconds) * intervalSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(bucketSeconds).UtcDateTime;
    }

    private static (int days, int intervalMinutes, bool useAllTime) ParseCustomDayPeriod(string period)
    {
        // Parse custom period like "45d", "120d"
        if (period.EndsWith("d") && int.TryParse(period[..^1], out var customDays))
        {
            var interval = customDays switch
            {
                <= 3 => 30,
                <= 7 => 60,
                <= 30 => 240,
                <= 90 => 720,
                _ => 1440
            };
            return (customDays, interval, false);
        }

        // Default to 7 days
        return (7, 60, false);
    }

    private static PlayerTrendsInsights CalculatePlayerTrendsInsights(
        PlayersOnlineDataPoint[] dataPoints,
        string period,
        int rollingWindowDays)
    {
        if (dataPoints.Length == 0)
        {
            return new PlayerTrendsInsights
            {
                OverallAverage = 0,
                RollingAverage = [],
                TrendDirection = "stable",
                PercentageChange = 0,
                PeakPlayers = 0,
                PeakTimestamp = DateTime.MinValue,
                LowestPlayers = 0,
                LowestTimestamp = DateTime.MinValue,
                CalculationMethod = "No data available"
            };
        }

        var overallAverage = dataPoints.Average(d => d.TotalPlayers);
        var peakDataPoint = dataPoints.MaxBy(d => d.TotalPlayers) ?? dataPoints[0];
        var lowestDataPoint = dataPoints.MinBy(d => d.TotalPlayers) ?? dataPoints[0];

        // Calculate rolling average
        var rollingAverage = CalculateRollingAverage(dataPoints, rollingWindowDays);

        // Calculate trend direction
        string trendDirection;
        double percentageChange = 0;

        if (dataPoints.Length >= 2)
        {
            var firstQuarter = dataPoints.Take(dataPoints.Length / 4).Average(d => d.TotalPlayers);
            var lastQuarter = dataPoints.Skip(dataPoints.Length * 3 / 4).Average(d => d.TotalPlayers);

            if (firstQuarter > 0)
            {
                percentageChange = ((lastQuarter - firstQuarter) / firstQuarter) * 100;
            }

            trendDirection = percentageChange switch
            {
                > 10 => "increasing",
                < -10 => "decreasing",
                _ => "stable"
            };
        }
        else
        {
            trendDirection = "stable";
        }

        return new PlayerTrendsInsights
        {
            OverallAverage = overallAverage,
            RollingAverage = rollingAverage,
            TrendDirection = trendDirection,
            PercentageChange = Math.Round(percentageChange, 2),
            PeakPlayers = peakDataPoint.TotalPlayers,
            PeakTimestamp = peakDataPoint.Timestamp,
            LowestPlayers = lowestDataPoint.TotalPlayers,
            LowestTimestamp = lowestDataPoint.Timestamp,
            CalculationMethod = GetCalculationMethodDescription(period)
        };
    }

    private static RollingAverageDataPoint[] CalculateRollingAverage(
        PlayersOnlineDataPoint[] dataPoints,
        int windowDays)
    {
        if (dataPoints.Length < 2 || windowDays <= 0)
        {
            return [];
        }

        var result = new List<RollingAverageDataPoint>();

        for (int i = 0; i < dataPoints.Length; i++)
        {
            var windowStart = dataPoints[i].Timestamp.AddDays(-windowDays);
            var windowData = dataPoints.Where(d =>
                d.Timestamp >= windowStart && d.Timestamp <= dataPoints[i].Timestamp).ToArray();

            if (windowData.Length > 0)
            {
                result.Add(new RollingAverageDataPoint
                {
                    Timestamp = dataPoints[i].Timestamp,
                    Average = windowData.Average(d => d.TotalPlayers)
                });
            }
        }

        return result.ToArray();
    }

    private static string GetCalculationMethodDescription(string period)
    {
        return period switch
        {
            "1d" => "5-minute average player counts",
            "3d" => "30-minute average player counts",
            "7d" => "Hourly average player counts",
            "1month" or "30d" => "4-hour average player counts",
            "3months" or "90d" => "12-hour average player counts",
            "6months" or "180d" or "1year" or "365d" => "Daily average player counts",
            "thisyear" or "alltime" => "Daily average player counts",
            _ => "Aggregated player counts"
        };
    }
}
