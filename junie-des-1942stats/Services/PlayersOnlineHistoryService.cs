using junie_des_1942stats.ClickHouse.Interfaces;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Services;

public class PlayersOnlineHistoryService
{
    private readonly IClickHouseReader _clickHouseReader;
    private readonly ILogger<PlayersOnlineHistoryService> _logger;

    public PlayersOnlineHistoryService(IClickHouseReader clickHouseReader, ILogger<PlayersOnlineHistoryService> logger)
    {
        _clickHouseReader = clickHouseReader;
        _logger = logger;
    }

    public async Task<PlayersOnlineHistoryResponse> GetPlayersOnlineHistory(string game, string period, int rollingWindowDays, string? serverGuid = null)
    {
        // Support both named periods and numeric day values (e.g., "90d", "180d")
        var (days, timeInterval, useAllTime) = period switch
        {
            "1d" => (1, "INTERVAL 5 MINUTE", false),
            "3d" => (3, "INTERVAL 30 MINUTE", false),
            "7d" => (7, "INTERVAL 1 HOUR", false),
            "1month" or "30d" => (30, "INTERVAL 4 HOUR", false),
            "3months" or "90d" => (90, "INTERVAL 12 HOUR", false),
            "6months" or "180d" => (180, "INTERVAL 1 DAY", false),
            "1year" or "365d" => (365, "INTERVAL 1 DAY", false),
            "thisyear" => (DateTime.Now.DayOfYear, "INTERVAL 1 DAY", false),
            "alltime" => (0, "INTERVAL 1 DAY", true),
            _ => ParseCustomDayPeriod(period)
        };

        var timeCondition = useAllTime
            ? ""
            : $"AND timestamp >= now() - INTERVAL {days} DAY";

        var serverCondition = !string.IsNullOrEmpty(serverGuid)
            ? $"AND server_guid = '{serverGuid.Replace("'", "''")}'"
            : "";

        var query = $@"
WITH server_bucket_counts AS (
    SELECT 
        toDateTime(toUnixTimestamp(timestamp) - (toUnixTimestamp(timestamp) % {GetIntervalSeconds(timeInterval)})) as time_bucket,
        server_guid,
        AVG(players_online) as avg_players_online
    FROM server_online_counts
    WHERE game = '{game.Replace("'", "''")}'
        {timeCondition}
        {serverCondition}
        AND timestamp < now()
    GROUP BY time_bucket, server_guid
)
SELECT 
    time_bucket,
    ROUND(SUM(avg_players_online)) as total_players
FROM server_bucket_counts
GROUP BY time_bucket
ORDER BY time_bucket
FORMAT TabSeparated";

        var result = await _clickHouseReader.ExecuteQueryAsync(query);
        var dataPoints = new List<PlayersOnlineDataPoint>();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 &&
                DateTime.TryParse(parts[0], out var timestamp) &&
                int.TryParse(parts[1], out var totalPlayers))
            {
                dataPoints.Add(new PlayersOnlineDataPoint
                {
                    Timestamp = timestamp,
                    TotalPlayers = totalPlayers
                });
            }
        }

        var insights = CalculatePlayerTrendsInsights(dataPoints.ToArray(), period, rollingWindowDays);

        return new PlayersOnlineHistoryResponse
        {
            DataPoints = dataPoints.ToArray(),
            Insights = insights,
            Period = period,
            Game = game,
            LastUpdated = DateTime.UtcNow.ToString("o")
        };
    }

    private static (int Days, string TimeInterval, bool UseAllTime) ParseCustomDayPeriod(string period)
    {
        // Try to parse custom day periods like "45d", "120d", etc.
        if (period.EndsWith("d") && int.TryParse(period[..^1], out var customDays))
        {
            var interval = customDays switch
            {
                <= 3 => "INTERVAL 30 MINUTE",
                <= 7 => "INTERVAL 1 HOUR",
                <= 30 => "INTERVAL 4 HOUR",
                <= 90 => "INTERVAL 12 HOUR",
                _ => "INTERVAL 1 DAY"
            };
            return (customDays, interval, false);
        }

        // Default fallback
        return (7, "INTERVAL 1 HOUR", false);
    }

    private static int GetIntervalSeconds(string timeInterval)
    {
        return timeInterval switch
        {
            "INTERVAL 5 MINUTE" => 300,
            "INTERVAL 30 MINUTE" => 1800,
            "INTERVAL 1 HOUR" => 3600,
            "INTERVAL 4 HOUR" => 14400,
            "INTERVAL 12 HOUR" => 43200,
            "INTERVAL 1 DAY" => 86400,
            _ => 3600
        };
    }

    private static PlayerTrendsInsights? CalculatePlayerTrendsInsights(PlayersOnlineDataPoint[] dataPoints, string period, int rollingWindowDays)
    {
        if (dataPoints.Length == 0) return null;

        var totalPlayers = dataPoints.Sum(dp => dp.TotalPlayers);
        var overallAverage = (double)totalPlayers / dataPoints.Length;

        var peakDataPoint = dataPoints.OrderByDescending(dp => dp.TotalPlayers).First();
        var lowestDataPoint = dataPoints.OrderBy(dp => dp.TotalPlayers).First();

        var startValue = dataPoints.First().TotalPlayers;
        var endValue = dataPoints.Last().TotalPlayers;
        var percentageChange = startValue == 0 ? 0 : ((double)(endValue - startValue) / startValue) * 100;

        var trendDirection = percentageChange switch
        {
            > 5 => "increasing",
            < -5 => "decreasing",
            _ => "stable"
        };

        var rollingAverage = CalculateRollingAverage(dataPoints, period, rollingWindowDays);
        var calculationMethod = GetCalculationMethodDescription(period);

        return new PlayerTrendsInsights
        {
            OverallAverage = Math.Round(overallAverage, 2),
            RollingAverage = rollingAverage,
            TrendDirection = trendDirection,
            PercentageChange = Math.Round(percentageChange, 2),
            PeakPlayers = peakDataPoint.TotalPlayers,
            PeakTimestamp = peakDataPoint.Timestamp,
            LowestPlayers = lowestDataPoint.TotalPlayers,
            LowestTimestamp = lowestDataPoint.Timestamp,
            CalculationMethod = calculationMethod
        };
    }

    private static RollingAverageDataPoint[] CalculateRollingAverage(PlayersOnlineDataPoint[] dataPoints, string period, int rollingWindowDays)
    {
        if (period == "1d" || period == "3d" || period == "7d" || dataPoints.Length < rollingWindowDays)
        {
            return [];
        }

        var result = new List<RollingAverageDataPoint>();

        for (int i = rollingWindowDays - 1; i < dataPoints.Length; i++)
        {
            var window = dataPoints.Skip(i - rollingWindowDays + 1).Take(rollingWindowDays).ToArray();
            var avg = window.Average(dp => dp.TotalPlayers);

            result.Add(new RollingAverageDataPoint
            {
                Timestamp = dataPoints[i].Timestamp,
                Average = Math.Round(avg, 2)
            });
        }

        return result.ToArray();
    }

    private static string GetCalculationMethodDescription(string period)
    {
        return period switch
        {
            "1d" => "5-minute interval sampling over 1 day",
            "3d" => "30-minute interval sampling over 3 days",
            "7d" => "Hourly sampling over 7 days",
            "1month" => "4-hour interval sampling with 7-day rolling average",
            "3months" => "12-hour interval sampling with 7-day rolling average",
            "thisyear" => "Daily sampling since start of year with 7-day rolling average",
            "alltime" => "Daily sampling since records began with 7-day rolling average",
            _ => "Custom period sampling"
        };
    }
}

