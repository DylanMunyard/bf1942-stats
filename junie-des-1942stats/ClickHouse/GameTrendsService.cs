using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class GameTrendsService : BaseClickHouseService
{
    private readonly ILogger<GameTrendsService> _logger;
    private readonly PlayerTrackerDbContext _dbContext;

    public GameTrendsService(HttpClient httpClient, string clickHouseUrl, ILogger<GameTrendsService> logger, PlayerTrackerDbContext dbContext)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    private async Task<List<T>> ReadAllAsync<T>(string query, params object[] parameters) where T : new()
    {
        var formattedQuery = SubstituteParameters(query, parameters) + " FORMAT TabSeparated";
        var result = await ExecuteQueryInternalAsync(formattedQuery);
        var items = new List<T>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            var item = ParseTabSeparatedLine<T>(parts);
            if (item != null)
                items.Add(item);
        }

        return items;
    }

    private async Task<T?> ReadSingleOrDefaultAsync<T>(string query, params object[] parameters) where T : new()
    {
        var formattedQuery = SubstituteParameters(query, parameters) + " LIMIT 1 FORMAT TabSeparated";
        var result = await ExecuteQueryInternalAsync(formattedQuery);
        
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return default(T);

        var parts = lines[0].Split('\t');
        return ParseTabSeparatedLine<T>(parts);
    }

    private static string SubstituteParameters(string query, params object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            return query;

        var result = query;
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramValue = parameters[i];
            string substitution;
            
            if (paramValue == null)
            {
                substitution = "NULL";
            }
            else if (paramValue is string stringValue)
            {
                // Escape single quotes in strings and wrap in quotes
                substitution = $"'{stringValue.Replace("'", "''")}'";
            }
            else if (paramValue is int || paramValue is long || paramValue is double || paramValue is decimal)
            {
                substitution = paramValue.ToString()!;
            }
            else if (paramValue is DateTime dateValue)
            {
                substitution = $"'{dateValue:yyyy-MM-dd HH:mm:ss}'";
            }
            else
            {
                // For other types, convert to string and treat as string
                substitution = $"'{paramValue.ToString()?.Replace("'", "''")}'";
            }
            
            // Replace the first occurrence of ? with the parameter value
            var questionIndex = result.IndexOf('?');
            if (questionIndex >= 0)
            {
                result = result.Substring(0, questionIndex) + substitution + result.Substring(questionIndex + 1);
            }
        }
        
        return result;
    }

    private static T? ParseTabSeparatedLine<T>(string[] parts) where T : new()
    {
        var item = new T();
        var properties = typeof(T).GetProperties();

        for (int i = 0; i < Math.Min(parts.Length, properties.Length); i++)
        {
            var property = properties[i];
            var value = parts[i];

            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                if (property.PropertyType == typeof(int))
                {
                    if (int.TryParse(value, out var intValue))
                        property.SetValue(item, intValue);
                }
                else if (property.PropertyType == typeof(double))
                {
                    if (double.TryParse(value, out var doubleValue))
                        property.SetValue(item, doubleValue);
                }
                else if (property.PropertyType == typeof(DateTime))
                {
                    if (DateTime.TryParse(value, out var dateValue))
                        property.SetValue(item, dateValue);
                }
                else if (property.PropertyType == typeof(string))
                {
                    property.SetValue(item, value);
                }
                else if (property.PropertyType == typeof(double[]))
                {
                    // Handle ClickHouse array format: [1.0, 2.0, 3.0]
                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        var arrayContent = value.Substring(1, value.Length - 2);
                        if (!string.IsNullOrWhiteSpace(arrayContent))
                        {
                            var elements = arrayContent.Split(',');
                            var doubleArray = new List<double>();
                            
                            foreach (var element in elements)
                            {
                                var trimmedElement = element.Trim();
                                if (double.TryParse(trimmedElement, out var doubleElement))
                                {
                                    doubleArray.Add(doubleElement);
                                }
                            }
                            
                            property.SetValue(item, doubleArray.ToArray());
                        }
                        else
                        {
                            property.SetValue(item, Array.Empty<double>());
                        }
                    }
                }
            }
            catch
            {
                // Skip invalid conversions
            }
        }

        return item;
    }

    /// <summary>
    /// Gets current activity status across games and servers using live SQLite sessions
    /// </summary>
    public async Task<List<CurrentActivityStatus>> GetCurrentActivityStatusAsync(string? game = null)
{
    // Use SQLite PlayerSessions for real-time data (sessions within last minute)
    var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
    
    var query = _dbContext.PlayerSessions
        .Include(ps => ps.Server)
        .Include(ps => ps.Player)
        .Where(ps => ps.IsActive && 
                    ps.LastSeenTime >= oneMinuteAgo &&
                    !ps.Player.AiBot); // Exclude bots from current activity
    
    // Add game filter if specified
    if (!string.IsNullOrEmpty(game))
    {
        query = query.Where(ps => ps.Server.Game == game);
    }
    
    var currentActivity = await query
        .GroupBy(ps => new { ps.Server.Game, ps.ServerGuid })
        .Where(g => g.Count() >= 2) // Only show servers with actual activity
        .Select(g => new CurrentActivityStatus
        {
            Game = g.Key.Game ?? "",
            ServerGuid = g.Key.ServerGuid,
            CurrentPlayers = g.Count(),
            LatestActivity = g.Max(ps => ps.LastSeenTime),
            CurrentMapName = g.OrderByDescending(ps => ps.LastSeenTime).First().MapName
        })
        .OrderByDescending(ca => ca.CurrentPlayers)
        .ThenByDescending(ca => ca.LatestActivity)
        .ToListAsync();

    return currentActivity;
}

    /// <summary>
    /// Gets weekly activity patterns to identify weekend vs weekday differences
    /// </summary>
    public async Task<List<WeeklyActivityPattern>> GetWeeklyActivityPatternsAsync(string? game = null, int daysPeriod = 30)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL ? DAY");
        var parameters = new List<object> { daysPeriod };

        if (!string.IsNullOrEmpty(game))
        {
            whereClause.Append(" AND game = ?");
            parameters.Add(game);
        }

        var query = $@"
            SELECT 
                toDayOfWeek(round_start_time) as day_of_week,
                toHour(round_start_time) as hour_of_day,
                COUNT(DISTINCT player_name) as unique_players,
                COUNT(*) as total_rounds,
                AVG(play_time_minutes) as avg_round_duration,
                CASE 
                    WHEN toDayOfWeek(round_start_time) IN (6, 7) THEN 'Weekend'
                    ELSE 'Weekday'
                END as period_type
            FROM player_rounds 
            {whereClause}
            GROUP BY day_of_week, hour_of_day, period_type
            ORDER BY day_of_week, hour_of_day";

        return await ReadAllAsync<WeeklyActivityPattern>(query, parameters.ToArray());
    }

    /// <summary>
    /// Gets trend insights to help players connect when servers are busy
    /// Provides "is it busy now?" and "will it get busier?" insights
    /// </summary>
    public async Task<SmartPredictionInsights> GetSmartPredictionInsightsAsync(string? game = null, int timeZoneOffsetHours = 0)
{
    var currentTime = DateTime.UtcNow.AddHours(timeZoneOffsetHours);
    var currentHour = currentTime.Hour;
    var currentDayOfWeek = (int)currentTime.DayOfWeek;
    // Convert to ClickHouse day format (Monday = 1, Sunday = 7)
    var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

    var whereClause = new StringBuilder();
    whereClause.Append("WHERE timestamp >= now() - INTERVAL 60 DAY");
    var parameters = new List<object>();

    if (!string.IsNullOrEmpty(game))
    {
        whereClause.Append(" AND game = ?");
        parameters.Add(game);
    }

    // Get 4-hour forecast: current hour + next 4 hours with correct day-of-week
    var forecastEntries = new List<(int hour, int dayOfWeek)>();
    for (int i = 0; i < 5; i++)
    {
        var futureTime = currentTime.AddHours(i);
        var futureHour = futureTime.Hour;
        var futureDayOfWeek = (int)futureTime.DayOfWeek;
        var futureClickHouseDayOfWeek = futureDayOfWeek == 0 ? 7 : futureDayOfWeek;
        forecastEntries.Add((futureHour, futureClickHouseDayOfWeek));
    }

    var fourHourForecastQuery = $@"
        SELECT 
            hour_of_day,
            day_of_week,
            AVG(total_players) as predicted_players,
            COUNT(*) as data_points
        FROM (
            SELECT 
                toHour(timestamp) as hour_of_day,
                toDayOfWeek(timestamp) as day_of_week,
                toDate(timestamp) as date,
                SUM(players_online) as total_players
            FROM server_online_counts 
            {whereClause}
            AND (hour_of_day, day_of_week) IN ({string.Join(",", forecastEntries.Select(e => $"({e.hour}, {e.dayOfWeek})"))})
            GROUP BY hour_of_day, day_of_week, date
        )
        GROUP BY hour_of_day, day_of_week
        ORDER BY hour_of_day, day_of_week";

    var fourHourForecast = await ReadAllAsync<HourlyPrediction>(fourHourForecastQuery, parameters.ToArray());

    // Get 24-hour peak analysis - find top 3 busiest hours in next 24 hours
    var next24Hours = new List<(int hour, int dayOfWeek)>();
    for (int i = 1; i <= 24; i++)
    {
        var futureTime = currentTime.AddHours(i);
        var futureHour = futureTime.Hour;
        var futureDayOfWeek = (int)futureTime.DayOfWeek;
        var futureClickHouseDayOfWeek = futureDayOfWeek == 0 ? 7 : futureDayOfWeek;
        next24Hours.Add((futureHour, futureClickHouseDayOfWeek));
    }

    // Group by unique hour-day combinations for the query
    var uniqueHourDayCombos = next24Hours.GroupBy(x => new { x.hour, x.dayOfWeek }).ToList();
    
    var peakHoursQuery = $@"
        WITH hourly_averages AS (
            SELECT 
                hour_of_day,
                day_of_week,
                AVG(total_players) as avg_players
            FROM (
                SELECT 
                    toHour(timestamp) as hour_of_day,
                    toDayOfWeek(timestamp) as day_of_week,
                    toDate(timestamp) as date,
                    SUM(players_online) as total_players
                FROM server_online_counts 
                {whereClause}
                GROUP BY hour_of_day, day_of_week, date
            )
            GROUP BY hour_of_day, day_of_week
            HAVING COUNT(*) >= 3  -- Ensure sufficient data points
        )
        SELECT 
            hour_of_day,
            day_of_week,
            avg_players as predicted_players
        FROM hourly_averages
        WHERE (hour_of_day, day_of_week) IN ({string.Join(",", uniqueHourDayCombos.Select(x => $"({x.Key.hour}, {x.Key.dayOfWeek})"))})
        ORDER BY avg_players DESC
        LIMIT 3";

    var peakHours = await ReadAllAsync<Peak24HourPrediction>(peakHoursQuery, parameters.ToArray());

    // Get actual current activity from player_metrics (real-time data)
    var realTimeActivityQuery = @"
        SELECT 
            COUNT(DISTINCT player_name) as predicted_players
        FROM player_metrics 
        WHERE timestamp >= now() - INTERVAL 1 MINUTE
            AND is_bot = 0";
    
    var realTimeParams = new List<object>();
    if (!string.IsNullOrEmpty(game))
    {
        realTimeActivityQuery += " AND game = ?";
        realTimeParams.Add(game);
    }

    var realTimeActivity = await ReadSingleOrDefaultAsync<HourlyPrediction>(realTimeActivityQuery, realTimeParams.ToArray());

    // Calculate insights
    var currentPredicted = realTimeActivity?.PredictedPlayers ?? 0;
    var nextHourPredicted = fourHourForecast.FirstOrDefault(f => f.HourOfDay == (currentHour + 1) % 24)?.PredictedPlayers ?? 0;
    var fourHourMaxPredicted = fourHourForecast.Skip(1).Any() ? fourHourForecast.Skip(1).Max(f => f?.PredictedPlayers ?? 0) : 0;

    string currentStatus;
    if (currentPredicted < 5) currentStatus = "very_quiet";
    else if (currentPredicted < 15) currentStatus = "quiet";
    else if (currentPredicted < 30) currentStatus = "moderate";
    else if (currentPredicted < 50) currentStatus = "busy";
    else currentStatus = "very_busy";

    string trendDirection;
    if (nextHourPredicted > currentPredicted * 1.2) trendDirection = "increasing_significantly";
    else if (nextHourPredicted > currentPredicted * 1.05) trendDirection = "increasing";
    else if (nextHourPredicted < currentPredicted * 0.8) trendDirection = "decreasing_significantly";
    else if (nextHourPredicted < currentPredicted * 0.95) trendDirection = "decreasing";
    else trendDirection = "stable";

    return new SmartPredictionInsights
    {
        CurrentHourPredictedPlayers = currentPredicted,
        CurrentStatus = currentStatus,
        TrendDirection = trendDirection,
        NextHourPredictedPlayers = nextHourPredicted,
        FourHourMaxPredictedPlayers = fourHourMaxPredicted,
        FourHourForecast = fourHourForecast.ToList(),
        Next24HourPeaks = peakHours.ToList(),
        PlayerTimeZoneOffsetHours = timeZoneOffsetHours,
        GeneratedAt = DateTime.UtcNow,
        RecommendationMessage = GenerateRecommendation(currentStatus, trendDirection, fourHourMaxPredicted, peakHours.FirstOrDefault())
    };
}

private static string GenerateRecommendation(string currentStatus, string trendDirection, double fourHourMax, Peak24HourPrediction? nextPeak)
{
    var recommendations = new List<string>();

    switch (currentStatus)
    {
        case "very_quiet":
        case "quiet":
            if (trendDirection.Contains("increasing"))
                recommendations.Add("🚀 Servers are quiet now but activity is picking up!");
            else if (fourHourMax > 20)
                recommendations.Add("📈 It's quiet now, but expect more players in the next few hours.");
            else
                recommendations.Add("😴 Servers are quiet right now.");
            break;
        case "moderate":
            if (trendDirection.Contains("increasing"))
                recommendations.Add("📈 Good activity level and growing - great time to join!");
            else
                recommendations.Add("✅ Decent player activity right now.");
            break;
        case "busy":
        case "very_busy":
            recommendations.Add("🔥 Servers are buzzing with activity - prime gaming time!");
            break;
    }

    if (nextPeak != null)
    {
        var peakTime = DateTime.UtcNow.Date.AddHours(nextPeak.HourOfDay);
        if (nextPeak.DayOfWeek == 7 && DateTime.UtcNow.DayOfWeek != DayOfWeek.Sunday)
            peakTime = peakTime.AddDays(1);
        else if ((int)DateTime.UtcNow.DayOfWeek + 1 != nextPeak.DayOfWeek && nextPeak.DayOfWeek != 7)
            peakTime = peakTime.AddDays(1);

        recommendations.Add($"⏰ Peak activity expected around {peakTime:HH:mm} with ~{nextPeak.PredictedPlayers:F0} players.");
    }

    return string.Join(" ", recommendations);
}

/// <summary>
/// Gets Google-style busy indicator comparing current activity to historical patterns
/// </summary>
public async Task<BusyIndicatorResult> GetBusyIndicatorAsync(string? game = null, int timeZoneOffsetHours = 0)
{
    var currentTime = DateTime.UtcNow.AddHours(timeZoneOffsetHours);
    var currentHour = currentTime.Hour;
    var currentDayOfWeek = (int)currentTime.DayOfWeek;
    var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

    var whereClause = new StringBuilder();
    var parameters = new List<object>();

    if (!string.IsNullOrEmpty(game))
    {
        whereClause.Append("AND game = ?");
        parameters.Add(game);
    }

    // Get current activity from player_metrics (real-time data within last minute)
    var currentActivityQuery = $@"
        SELECT 
            COUNT(DISTINCT player_name) as current_players
        FROM player_metrics 
        WHERE timestamp >= now() - INTERVAL 1 MINUTE
            AND is_bot = 0
            {whereClause}";

    var currentActivity = await ReadSingleOrDefaultAsync<CurrentBusyMetrics>(currentActivityQuery, parameters.ToArray());
    var currentPlayers = currentActivity?.CurrentPlayers ?? 0;

    // Reset whereClause and parameters for historical query using server_online_counts
    whereClause = new StringBuilder();
    whereClause.Append("WHERE timestamp >= now() - INTERVAL 60 DAY");
    parameters = new List<object>();

    if (!string.IsNullOrEmpty(game))
    {
        whereClause.Append(" AND game = ?");
        parameters.Add(game);
    }

    // Simplified approach using ClickHouse native aggregations
    var historicalQuery = $@"
        SELECT 
            groupArray(hourly_total) as daily_averages
        FROM (
            SELECT 
                toDate(timestamp) as date_key,
                toHour(timestamp) as hour_key,
                -- Use argMax to get the most recent player count for each server in each 15-minute window
                -- Then sum across all servers to get total players for that time window
                -- Finally average all time windows within the hour
                AVG(players_total) as hourly_total
            FROM (
                SELECT 
                    toDate(timestamp) as date_key,
                    toHour(timestamp) as hour_key,
                    toStartOfInterval(timestamp, INTERVAL 15 MINUTE) as time_window,
                    -- Sum the most recent count from each server in this 15-minute window
                    SUM(argMax(players_online, timestamp)) as players_total
                FROM server_online_counts
                {whereClause}
                    AND toHour(timestamp) = ?
                    AND toDayOfWeek(timestamp) = ?
                GROUP BY date_key, hour_key, time_window, server_guid
                HAVING players_total > 0
            ) time_window_totals
            GROUP BY date_key, hour_key
            HAVING COUNT(*) >= 2  -- Require at least 2 time windows per hour for reliable data
        )";

    var histParams = parameters.Concat(new object[] { currentHour, clickHouseDayOfWeek }).ToArray();
    var dailyTotalsResults = await ReadAllAsync<DailyAveragesResult>(historicalQuery, histParams);
    var dailyTotalsResult = dailyTotalsResults.FirstOrDefault();
    
    if (dailyTotalsResult?.DailyAverages == null || dailyTotalsResult.DailyAverages.Length < 3)
    {
        return new BusyIndicatorResult
        {
            BusyLevel = "unknown",
            BusyText = "Not enough data",
            CurrentPlayers = currentPlayers,
            TypicalPlayers = 0,
            Percentile = 0,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // Calculate statistics in application code
    var averages = dailyTotalsResult.DailyAverages.OrderBy(x => x).ToArray();
    var count = averages.Length;
    
    var avgPlayers = averages.Average();
    var minPlayers = averages.Min();
    var maxPlayers = averages.Max();
    var medianPlayers = count % 2 == 0 
        ? (averages[count / 2 - 1] + averages[count / 2]) / 2.0
        : averages[count / 2];
    var q25Players = averages[(int)(count * 0.25)];
    var q75Players = averages[(int)(count * 0.75)];
    var q90Players = averages[(int)(count * 0.90)];

    var historicalStats = new HistoricalBusyStats
    {
        AvgPlayers = avgPlayers,
        Q25Players = q25Players,
        MedianPlayers = medianPlayers,
        Q75Players = q75Players,
        Q90Players = q90Players,
        MinPlayers = minPlayers,
        MaxPlayers = maxPlayers,
        DataPoints = count
    };

    // Calculate percentile and busy level
    string busyLevel;
    string busyText;
    double percentile = 0;

    if (currentPlayers >= historicalStats.Q90Players)
    {
        busyLevel = "very_busy";
        busyText = "Busier than usual";
        percentile = 90;
    }
    else if (currentPlayers >= historicalStats.Q75Players)
    {
        busyLevel = "busy";
        busyText = "Busy";
        percentile = 75;
    }
    else if (currentPlayers >= historicalStats.MedianPlayers)
    {
        busyLevel = "moderate";
        busyText = "As busy as usual";
        percentile = 50;
    }
    else if (currentPlayers >= historicalStats.Q25Players)
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

    // Add context for extreme cases
    if (currentPlayers >= historicalStats.MaxPlayers * 0.95)
    {
        busyText = "Extremely busy - peak activity!";
    }
    else if (currentPlayers <= historicalStats.MinPlayers * 1.1 && historicalStats.MinPlayers > 0)
    {
        busyText = "Very quiet right now";
    }

    return new BusyIndicatorResult
    {
        BusyLevel = busyLevel,
        BusyText = busyText,
        CurrentPlayers = currentPlayers,
        TypicalPlayers = historicalStats.AvgPlayers,
        Percentile = percentile,
        HistoricalRange = new HistoricalRange
        {
            Min = historicalStats.MinPlayers,
            Q25 = historicalStats.Q25Players,
            Median = historicalStats.MedianPlayers,
            Q75 = historicalStats.Q75Players,
            Q90 = historicalStats.Q90Players,
            Max = historicalStats.MaxPlayers,
            Average = historicalStats.AvgPlayers
        },
        GeneratedAt = DateTime.UtcNow
    };
}

public async Task<GroupedServerBusyIndicatorResult> GetServerBusyIndicatorAsync(string[] serverGuids, int timeZoneOffsetHours = 0)
{
    var currentTime = DateTime.UtcNow.AddHours(timeZoneOffsetHours);
    var currentHour = currentTime.Hour;
    var currentDayOfWeek = (int)currentTime.DayOfWeek;
    var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

    // Create server GUID list for IN clause
    var serverGuidList = string.Join(",", serverGuids.Select(sg => $"'{sg}'"));

    // Single query to get current activity for all servers
    var currentActivityQuery = $@"
        SELECT 
            server_guid,
            COUNT(DISTINCT player_name) as current_players
        FROM player_metrics 
        WHERE timestamp >= now() - INTERVAL 1 MINUTE
            AND is_bot = 0
            AND server_guid IN ({serverGuidList})
        GROUP BY server_guid";

    var currentActivities = await ReadAllAsync<ServerCurrentActivity>(currentActivityQuery);

    // Single query to get server info for all servers
    var serverInfoQuery = $@"
        SELECT DISTINCT
            server_guid,
            argMax(server_name, timestamp) as server_name,
            argMax(game, timestamp) as game
        FROM player_metrics
        WHERE server_guid IN ({serverGuidList})
        GROUP BY server_guid";

    var serverInfos = await ReadAllAsync<GameTrendsServerInfo>(serverInfoQuery);

    // Single query to get historical data for all servers
    var historicalQuery = $@"
        SELECT 
            server_guid,
            groupArray(hourly_avg) as daily_averages
        FROM (
            SELECT 
                server_guid,
                toDate(timestamp) as date_key,
                AVG(players_online) as hourly_avg
            FROM server_online_counts
            WHERE timestamp >= now() - INTERVAL 60 DAY 
                AND server_guid IN ({serverGuidList})
                AND toHour(timestamp) = ?
                AND toDayOfWeek(timestamp) = ?
            GROUP BY server_guid, date_key, toStartOfInterval(timestamp, INTERVAL 15 MINUTE)
            HAVING hourly_avg > 0
        )
        GROUP BY server_guid";

    var historicalData = await ReadAllAsync<ServerHistoricalData>(historicalQuery, new object[] { currentHour, clickHouseDayOfWeek });

    // Build results
    var serverResults = new List<ServerBusyIndicatorResult>();

    foreach (var serverGuid in serverGuids)
    {
        var currentActivity = currentActivities.FirstOrDefault(ca => ca.ServerGuid == serverGuid);
        var serverInfo = serverInfos.FirstOrDefault(si => si.ServerGuid == serverGuid);
        var historical = historicalData.FirstOrDefault(hd => hd.ServerGuid == serverGuid);

        var currentPlayers = currentActivity?.CurrentPlayers ?? 0;

        BusyIndicatorResult busyIndicator;

        if (historical?.DailyAverages == null || historical.DailyAverages.Length < 3)
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
            // Calculate statistics
            var averages = historical.DailyAverages.OrderBy(x => x).ToArray();
            var count = averages.Length;
            
            var avgPlayers = averages.Average();
            var minPlayers = averages.Min();
            var maxPlayers = averages.Max();
            var medianPlayers = count % 2 == 0 
                ? (averages[count / 2 - 1] + averages[count / 2]) / 2.0
                : averages[count / 2];
            var q25Players = averages[(int)(count * 0.25)];
            var q75Players = averages[(int)(count * 0.75)];
            var q90Players = averages[(int)(count * 0.90)];

            // Calculate percentile and busy level
            string busyLevel;
            string busyText;
            double percentile = 0;

            if (currentPlayers >= q90Players)
            {
                busyLevel = "very_busy";
                busyText = "Busier than usual";
                percentile = 90;
            }
            else if (currentPlayers >= q75Players)
            {
                busyLevel = "busy";
                busyText = "Busy";
                percentile = 75;
            }
            else if (currentPlayers >= medianPlayers)
            {
                busyLevel = "moderate";
                busyText = "As busy as usual";
                percentile = 50;
            }
            else if (currentPlayers >= q25Players)
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

            // Add context for extreme cases
            if (currentPlayers >= maxPlayers * 0.95)
            {
                busyText = "Extremely busy - peak activity!";
            }
            else if (currentPlayers <= minPlayers * 1.1 && minPlayers > 0)
            {
                busyText = "Very quiet right now";
            }

            busyIndicator = new BusyIndicatorResult
            {
                BusyLevel = busyLevel,
                BusyText = busyText,
                CurrentPlayers = currentPlayers,
                TypicalPlayers = avgPlayers,
                Percentile = percentile,
                HistoricalRange = new HistoricalRange
                {
                    Min = minPlayers,
                    Q25 = q25Players,
                    Median = medianPlayers,
                    Q75 = q75Players,
                    Q90 = q90Players,
                    Max = maxPlayers,
                    Average = avgPlayers
                },
                GeneratedAt = DateTime.UtcNow
            };
        }

        serverResults.Add(new ServerBusyIndicatorResult
        {
            ServerGuid = serverGuid,
            ServerName = serverInfo?.ServerName ?? "Unknown Server",
            Game = serverInfo?.Game ?? "Unknown",
            BusyIndicator = busyIndicator
        });
    }

    return new GroupedServerBusyIndicatorResult
    {
        ServerResults = serverResults,
        GeneratedAt = DateTime.UtcNow
    };
}
}

// Data models for trend analysis

public class CurrentActivityStatus
{
    public string Game { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public DateTime LatestActivity { get; set; }
    public string CurrentMapName { get; set; } = "";
}

public class WeeklyActivityPattern
{
    public int DayOfWeek { get; set; }
    public int HourOfDay { get; set; }
    public int UniquePlayers { get; set; }
    public int TotalRounds { get; set; }
    public double AvgRoundDuration { get; set; }
    public string PeriodType { get; set; } = ""; // Weekend/Weekday
}

public class TrendInsights
{
    public double CurrentHourAvgPlayers { get; set; }
    public double CurrentHourAvgRounds { get; set; }
    public double NextHourAvgPlayers { get; set; }
    public double NextHourAvgRounds { get; set; }
    public string TrendDirection { get; set; } = ""; // "increasing" or "decreasing"
    public int PlayerTimeZoneOffsetHours { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public class ActivityMetrics
{
    public double AvgCurrentPlayers { get; set; }
    public double AvgCurrentRounds { get; set; }
}

// New data models for smart prediction insights
public class SmartPredictionInsights
{
    public double CurrentHourPredictedPlayers { get; set; }
    public string CurrentStatus { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy
    public string TrendDirection { get; set; } = ""; // increasing_significantly, increasing, stable, decreasing, decreasing_significantly
    public double NextHourPredictedPlayers { get; set; }
    public double FourHourMaxPredictedPlayers { get; set; }
    public List<HourlyPrediction> FourHourForecast { get; set; } = new();
    public List<Peak24HourPrediction> Next24HourPeaks { get; set; } = new();
    public int PlayerTimeZoneOffsetHours { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string RecommendationMessage { get; set; } = "";
}

public class HourlyPrediction
{
    public int HourOfDay { get; set; }
    public double PredictedPlayers { get; set; }
    public int DataPoints { get; set; }
}

public class Peak24HourPrediction
{
    public int HourOfDay { get; set; }
    public int DayOfWeek { get; set; }
    public double PredictedPlayers { get; set; }
}

// Google-style busy indicator data models
public class BusyIndicatorResult
{
    public string BusyLevel { get; set; } = ""; // very_quiet, quiet, moderate, busy, very_busy, unknown
    public string BusyText { get; set; } = ""; // Human-readable text like "Busier than usual"
    public double CurrentPlayers { get; set; }
    public double TypicalPlayers { get; set; }
    public double Percentile { get; set; } // What percentile the current activity falls into
    public HistoricalRange? HistoricalRange { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public class ServerBusyIndicatorResult
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Game { get; set; } = "";
    public BusyIndicatorResult BusyIndicator { get; set; } = new();
}

public class GroupedServerBusyIndicatorResult
{
    public List<ServerBusyIndicatorResult> ServerResults { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class GameTrendsServerInfo
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Game { get; set; } = "";
}

public class HistoricalRange
{
    public double Min { get; set; }
    public double Q25 { get; set; }
    public double Median { get; set; }
    public double Q75 { get; set; }
    public double Q90 { get; set; }
    public double Max { get; set; }
    public double Average { get; set; }
}

public class CurrentBusyMetrics
{
    public double CurrentPlayers { get; set; }
}

public class HistoricalBusyStats
{
    public double AvgPlayers { get; set; }
    public double Q25Players { get; set; }
    public double MedianPlayers { get; set; }
    public double Q75Players { get; set; }
    public double Q90Players { get; set; }
    public double MaxPlayers { get; set; }
    public double MinPlayers { get; set; }
    public int DataPoints { get; set; }
}

public class DailyAveragesResult
{
    public double[] DailyAverages { get; set; } = Array.Empty<double>();
}

public class ServerCurrentActivity
{
    public string ServerGuid { get; set; } = "";
    public double CurrentPlayers { get; set; }
}

public class ServerHistoricalData
{
    public string ServerGuid { get; set; } = "";
    public double[] DailyAverages { get; set; } = Array.Empty<double>();
}
