using api.ClickHouse.Base;
using api.ClickHouse.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.Extensions.Logging;

namespace api.ClickHouse;

public class GameTrendsService : BaseClickHouseService, IGameTrendsService
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
    public async Task<List<CurrentActivityStatus>> GetCurrentActivityStatusAsync(string? game = null, string[]? serverGuids = null)
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

        // Add server GUIDs filter if specified
        if (serverGuids != null && serverGuids.Length > 0)
        {
            query = query.Where(ps => serverGuids.Contains(ps.ServerGuid));
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
    public async Task<SmartPredictionInsights> GetSmartPredictionInsightsAsync(string? game = null)
    {
        var currentTime = DateTime.UtcNow;
        var currentHour = currentTime.Hour;
        var currentDayOfWeek = (int)currentTime.DayOfWeek;
        // Convert to ClickHouse day format (Monday = 1, Sunday = 7)
        var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

        // Get current player count from SQLite using the same logic as GetCurrentActivityStatusAsync
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var currentPlayerQuery = _dbContext.PlayerSessions
            .Include(ps => ps.Server)
            .Include(ps => ps.Player)
            .Where(ps => ps.IsActive &&
                        ps.LastSeenTime >= oneMinuteAgo &&
                        !ps.Player.AiBot); // Exclude bots from current activity

        // Add game filter if specified
        if (!string.IsNullOrEmpty(game))
        {
            currentPlayerQuery = currentPlayerQuery.Where(ps => ps.Server.Game == game);
        }

        var currentActualPlayers = await currentPlayerQuery.CountAsync();

        var whereClause = new StringBuilder();
        whereClause.Append("WHERE timestamp >= now() - INTERVAL 60 DAY");
        var parameters = new List<object>();

        if (!string.IsNullOrEmpty(game))
        {
            whereClause.Append(" AND game = ?");
            parameters.Add(game);
        }

        // Get 8-hour forecast: current hour + next 8 hours with correct day-of-week
        var forecastEntries = new List<(int hour, int dayOfWeek)>();
        for (int i = 0; i < 9; i++)
        {
            var futureTime = currentTime.AddHours(i);
            var futureHour = futureTime.Hour;
            var futureDayOfWeek = (int)futureTime.DayOfWeek;
            var futureClickHouseDayOfWeek = futureDayOfWeek == 0 ? 7 : futureDayOfWeek;
            forecastEntries.Add((futureHour, futureClickHouseDayOfWeek));
        }

        var forecastQuery = $@"
        SELECT 
            hour_of_day,
            day_of_week,
            AVG(hourly_total) as predicted_players,
            COUNT(*) as data_points
        FROM (
            SELECT 
                hour_of_day,
                day_of_week,
                date_key,
                SUM(latest_players_per_server) as hourly_total
            FROM (
                SELECT 
                    toHour(timestamp) as hour_of_day,
                    toDayOfWeek(timestamp) as day_of_week,
                    toDate(timestamp) as date_key,
                    server_guid,
                    argMax(players_online, timestamp) as latest_players_per_server
                FROM server_online_counts
                {whereClause}
                    AND (toHour(timestamp), toDayOfWeek(timestamp)) IN ({string.Join(",", forecastEntries.Select(e => $"({e.hour}, {e.dayOfWeek})"))})
                GROUP BY toHour(timestamp), toDayOfWeek(timestamp), toDate(timestamp), server_guid
            )
            GROUP BY hour_of_day, day_of_week, date_key
        )
        GROUP BY hour_of_day, day_of_week
        ORDER BY hour_of_day, day_of_week";

        var forecast = await ReadAllAsync<HourlyPrediction>(forecastQuery, parameters.ToArray());

        // Populate current hour data and delta for "Now" bucket
        foreach (var forecastEntry in forecast)
        {
            if (forecastEntry.HourOfDay == currentHour && forecastEntry.DayOfWeek == clickHouseDayOfWeek)
            {
                forecastEntry.IsCurrentHour = true;
                forecastEntry.ActualPlayers = currentActualPlayers;
                forecastEntry.Delta = currentActualPlayers - forecastEntry.PredictedPlayers;
            }
            else
            {
                forecastEntry.IsCurrentHour = false;
            }
        }


        // Calculate insights - use current hour prediction from forecast instead of real-time data
        var currentHourPredicted = forecast.FirstOrDefault(f => f.HourOfDay == currentHour && f.DayOfWeek == clickHouseDayOfWeek)?.PredictedPlayers ?? 0;

        // Get next hour prediction - look for the next hour in the forecast
        var nextHourTime = currentTime.AddHours(1);
        var nextHourClickHouseDayOfWeek = (int)nextHourTime.DayOfWeek == 0 ? 7 : (int)nextHourTime.DayOfWeek;
        var nextHourPredicted = forecast.FirstOrDefault(f => f.HourOfDay == nextHourTime.Hour && f.DayOfWeek == nextHourClickHouseDayOfWeek)?.PredictedPlayers ?? 0;

        // Get max prediction from the forecast (skip current hour)
        var maxPredictedPlayers = forecast.Skip(1).Any() ? forecast.Skip(1).Max(f => f?.PredictedPlayers ?? 0) : 0;

        // Compare actual vs predicted to determine activity status
        string activityComparison;
        if (currentHourPredicted > 0)
        {
            var ratio = (double)currentActualPlayers / currentHourPredicted;
            if (ratio > 1.3) activityComparison = "busier_than_usual";
            else if (ratio < 0.7) activityComparison = "quieter_than_usual";
            else activityComparison = "as_usual";
        }
        else
        {
            activityComparison = currentActualPlayers > 5 ? "busier_than_usual" : "as_usual";
        }

        string currentStatus;
        if (currentActualPlayers < 5) currentStatus = "very_quiet";
        else if (currentActualPlayers < 15) currentStatus = "quiet";
        else if (currentActualPlayers < 30) currentStatus = "moderate";
        else if (currentActualPlayers < 50) currentStatus = "busy";
        else currentStatus = "very_busy";

        string trendDirection;
        if (nextHourPredicted > currentHourPredicted * 1.2) trendDirection = "increasing_significantly";
        else if (nextHourPredicted > currentHourPredicted * 1.05) trendDirection = "increasing";
        else if (nextHourPredicted < currentHourPredicted * 0.8) trendDirection = "decreasing_significantly";
        else if (nextHourPredicted < currentHourPredicted * 0.95) trendDirection = "decreasing";
        else trendDirection = "stable";

        return new SmartPredictionInsights
        {
            CurrentHourPredictedPlayers = currentHourPredicted,
            CurrentActualPlayers = currentActualPlayers,
            ActivityComparisonStatus = activityComparison,
            CurrentStatus = currentStatus,
            TrendDirection = trendDirection,
            NextHourPredictedPlayers = nextHourPredicted,
            MaxPredictedPlayers = maxPredictedPlayers,
            Forecast = forecast.ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }


    public async Task<GroupedServerBusyIndicatorResult> GetServerBusyIndicatorAsync(string[] serverGuids, int timelineHourRange = 4)
    {
        var currentTime = DateTime.UtcNow;
        var currentHour = currentTime.Hour;
        var currentDayOfWeek = (int)currentTime.DayOfWeek;
        var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

        // Get current activity using the GetCurrentActivityStatusAsync method
        var currentActivityStatuses = await GetCurrentActivityStatusAsync(serverGuids: serverGuids);

        // Convert to the format expected by the rest of the method
        var currentActivities = currentActivityStatuses.Select(cas => new ServerCurrentActivity
        {
            ServerGuid = cas.ServerGuid,
            CurrentPlayers = cas.CurrentPlayers
        }).ToList();

        // Create server GUID list for IN clause
        var serverGuidList = string.Join(",", serverGuids.Select(sg => $"'{sg}'"));

        // Get server info from SQLite instead of expensive ClickHouse query
        // This replaces a potentially expensive scan of player_metrics table
        var serverInfos = await _dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .Select(s => new GameTrendsServerInfo
            {
                ServerGuid = s.Guid,
                ServerName = s.Name,
                Game = s.GameId
            })
            .ToListAsync();

        // Single query to get historical data for all servers for current hour
        // Directly average players_online by hour - much simpler and more efficient
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
            GROUP BY server_guid, date_key
            HAVING hourly_avg > 0
        )
        GROUP BY server_guid";

        var historicalData = await ReadAllAsync<ServerHistoricalData>(historicalQuery, new object[] { currentHour, clickHouseDayOfWeek });

        // Query to get hourly timeline data per server (configurable hours before and after current hour)
        var timelineHours = new List<int>();
        for (int i = -timelineHourRange; i <= timelineHourRange; i++)
        {
            var hour = (currentHour + i + 24) % 24;
            timelineHours.Add(hour);
        }

        var timelineQuery = $@"
        SELECT 
            server_guid,
            toHour(timestamp) as hour,
            AVG(players_online) as avg_players
        FROM server_online_counts
        WHERE timestamp >= now() - INTERVAL 30 DAY 
            AND server_guid IN ({serverGuidList})
            AND toDayOfWeek(timestamp) = ?
            AND toHour(timestamp) IN ({string.Join(",", timelineHours)})
        GROUP BY server_guid, toHour(timestamp)
        ORDER BY server_guid, hour";

        var timelineData = await ReadAllAsync<ServerHourlyTimelineData>(timelineQuery, new object[] { clickHouseDayOfWeek });

        // Build results
        var serverResults = new List<ServerBusyIndicatorResult>();

        foreach (var serverGuid in serverGuids)
        {
            var currentActivity = currentActivities.FirstOrDefault(ca => ca.ServerGuid == serverGuid);
            var serverInfo = serverInfos.FirstOrDefault(si => si.ServerGuid == serverGuid);
            var historical = historicalData.FirstOrDefault(hd => hd.ServerGuid == serverGuid);
            var serverTimelineData = timelineData.Where(td => td.ServerGuid == serverGuid).ToList();

            var currentPlayers = currentActivity?.CurrentPlayers ?? 0;

            // Build hourly timeline for this server
            var serverHourlyTimeline = new List<HourlyBusyData>();
            foreach (var hour in timelineHours)
            {
                var hourData = serverTimelineData.FirstOrDefault(td => td.Hour == hour);
                var avgPlayers = hourData?.AvgPlayers ?? 0;

                // Calculate busy level based on percentile logic (consistent with main calculation)
                string busyLevel;
                if (avgPlayers >= 20) busyLevel = "very_busy";
                else if (avgPlayers >= 15) busyLevel = "busy";
                else if (avgPlayers >= 10) busyLevel = "moderate";
                else if (avgPlayers >= 5) busyLevel = "quiet";
                else busyLevel = "very_quiet";

                serverHourlyTimeline.Add(new HourlyBusyData
                {
                    Hour = hour,
                    TypicalPlayers = avgPlayers,
                    BusyLevel = busyLevel,
                    IsCurrentHour = hour == currentHour
                });
            }

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
                BusyIndicator = busyIndicator,
                HourlyTimeline = serverHourlyTimeline
            });
        }

        return new GroupedServerBusyIndicatorResult
        {
            ServerResults = serverResults,
            GeneratedAt = DateTime.UtcNow
        };
    }
}
