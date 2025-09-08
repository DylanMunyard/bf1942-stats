using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
using System.Text;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class GameTrendsService : BaseClickHouseService
{
    private readonly ILogger<GameTrendsService> _logger;

    public GameTrendsService(HttpClient httpClient, string clickHouseUrl, ILogger<GameTrendsService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
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
        var formattedQuery = SubstituteParameters(query, parameters) + " FORMAT TabSeparated LIMIT 1";
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
            }
            catch
            {
                // Skip invalid conversions
            }
        }

        return item;
    }

    /// <summary>
    /// Gets hourly activity trends for the past month, helping players understand busy periods
    /// </summary>
    public async Task<List<HourlyActivityTrend>> GetHourlyActivityTrendsAsync(string? gameId = null, int daysPeriod = 30)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL ? DAY");
        var parameters = new List<object> { daysPeriod };

        if (!string.IsNullOrEmpty(gameId))
        {
            whereClause.Append(" AND game_id = ?");
            parameters.Add(gameId);
        }

        // Get hourly activity patterns with timezone handling for display
        var query = $@"
            SELECT 
                toHour(round_start_time) as hour_of_day,
                toDayOfWeek(round_start_time) as day_of_week,
                COUNT(DISTINCT player_name) as unique_players,
                COUNT(*) as total_rounds,
                AVG(play_time_minutes) as avg_round_duration,
                COUNT(DISTINCT server_guid) as active_servers,
                uniqExact(map_name) as unique_maps
            FROM player_rounds 
            {whereClause}
            GROUP BY hour_of_day, day_of_week
            ORDER BY day_of_week, hour_of_day";

        return await ReadAllAsync<HourlyActivityTrend>(query, parameters.ToArray());
    }

    /// <summary>
    /// Gets server activity trends to identify which servers are busiest at different times
    /// </summary>
    public async Task<List<ServerActivityTrend>> GetServerActivityTrendsAsync(string? gameId = null, int daysPeriod = 7)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL ? DAY");
        var parameters = new List<object> { daysPeriod };

        if (!string.IsNullOrEmpty(gameId))
        {
            whereClause.Append(" AND game_id = ?");
            parameters.Add(gameId);
        }

        var query = $@"
            SELECT 
                server_guid,
                toHour(round_start_time) as hour_of_day,
                COUNT(DISTINCT player_name) as unique_players,
                COUNT(*) as total_rounds,
                AVG(play_time_minutes) as avg_round_duration,
                uniqExact(map_name) as unique_maps,
                max(round_start_time) as last_activity
            FROM player_rounds 
            {whereClause}
            GROUP BY server_guid, hour_of_day
            HAVING unique_players >= 4  -- Only include servers with meaningful activity
            ORDER BY hour_of_day, unique_players DESC";

        return await ReadAllAsync<ServerActivityTrend>(query, parameters.ToArray());
    }

    /// <summary>
    /// Gets current activity status across games and servers
    /// </summary>
    public async Task<List<CurrentActivityStatus>> GetCurrentActivityStatusAsync()
    {
        // Look at activity in the last hour to determine "current" status
        var query = @"
            SELECT 
                game_id,
                server_guid,
                COUNT(DISTINCT player_name) as current_players,
                COUNT(*) as active_rounds,
                max(round_start_time) as latest_activity,
                uniqExact(map_name) as maps_in_rotation
            FROM player_rounds 
            WHERE round_start_time >= now() - INTERVAL 1 HOUR
            GROUP BY game_id, server_guid
            HAVING current_players >= 2  -- Only show servers with actual activity
            ORDER BY current_players DESC, latest_activity DESC";

        return await ReadAllAsync<CurrentActivityStatus>(query);
    }

    /// <summary>
    /// Gets weekly activity patterns to identify weekend vs weekday differences
    /// </summary>
    public async Task<List<WeeklyActivityPattern>> GetWeeklyActivityPatternsAsync(string? gameId = null, int daysPeriod = 30)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL ? DAY");
        var parameters = new List<object> { daysPeriod };

        if (!string.IsNullOrEmpty(gameId))
        {
            whereClause.Append(" AND game_id = ?");
            parameters.Add(gameId);
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
    /// Gets game mode popularity trends (like CTF events mentioned in the issue)
    /// </summary>
    public async Task<List<GameModeActivityTrend>> GetGameModeActivityTrendsAsync(string? gameId = null, int daysPeriod = 30)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL ? DAY");
        var parameters = new List<object> { daysPeriod };

        if (!string.IsNullOrEmpty(gameId))
        {
            whereClause.Append(" AND game_id = ?");
            parameters.Add(gameId);
        }

        // Use map_name as a proxy for game mode since some maps indicate specific modes like CTF
        var query = $@"
            SELECT 
                map_name,
                game_id,
                toDayOfWeek(round_start_time) as day_of_week,
                toHour(round_start_time) as hour_of_day,
                COUNT(DISTINCT player_name) as unique_players,
                COUNT(*) as total_rounds,
                AVG(play_time_minutes) as avg_round_duration,
                COUNT(DISTINCT server_guid) as servers_hosting
            FROM player_rounds 
            {whereClause}
            GROUP BY map_name, game_id, day_of_week, hour_of_day
            HAVING total_rounds >= 3  -- Filter out one-off activities
            ORDER BY total_rounds DESC, unique_players DESC";

        return await ReadAllAsync<GameModeActivityTrend>(query, parameters.ToArray());
    }

    /// <summary>
    /// Gets trend insights to help players connect when servers are busy
    /// Provides "is it busy now?" and "will it get busier?" insights
    /// </summary>
    public async Task<TrendInsights> GetTrendInsightsAsync(string? gameId = null, int timeZoneOffsetHours = 0)
    {
        var currentHour = DateTime.UtcNow.AddHours(timeZoneOffsetHours).Hour;
        var currentDayOfWeek = (int)DateTime.UtcNow.AddHours(timeZoneOffsetHours).DayOfWeek;
        // Convert to ClickHouse day format (Monday = 1, Sunday = 7)
        var clickHouseDayOfWeek = currentDayOfWeek == 0 ? 7 : currentDayOfWeek;

        var whereClause = new StringBuilder();
        whereClause.Append("WHERE round_start_time >= now() - INTERVAL 30 DAY");
        var parameters = new List<object>();

        if (!string.IsNullOrEmpty(gameId))
        {
            whereClause.Append(" AND game_id = ?");
            parameters.Add(gameId);
        }

        // Get current hour activity
        var currentActivityQuery = $@"
            SELECT 
                AVG(unique_players) as avg_current_players,
                AVG(total_rounds) as avg_current_rounds
            FROM (
                SELECT 
                    toDate(round_start_time) as date,
                    COUNT(DISTINCT player_name) as unique_players,
                    COUNT(*) as total_rounds
                FROM player_rounds 
                {whereClause}
                AND toHour(round_start_time) = ?
                AND toDayOfWeek(round_start_time) = ?
                GROUP BY date
            )";

        parameters.Add(currentHour);
        parameters.Add(clickHouseDayOfWeek);

        var currentActivity = await ReadSingleOrDefaultAsync<ActivityMetrics>(currentActivityQuery, parameters.ToArray()) 
            ?? new ActivityMetrics();

        // Get next hour activity prediction
        var nextHour = (currentHour + 1) % 24;
        var nextHourQuery = $@"
            SELECT 
                AVG(unique_players) as avg_current_players,
                AVG(total_rounds) as avg_current_rounds
            FROM (
                SELECT 
                    toDate(round_start_time) as date,
                    COUNT(DISTINCT player_name) as unique_players,
                    COUNT(*) as total_rounds
                FROM player_rounds 
                {whereClause.ToString().Replace("AND toDayOfWeek(round_start_time) = ?", "AND toDayOfWeek(round_start_time) = ?")}
                AND toHour(round_start_time) = ?
                AND toDayOfWeek(round_start_time) = ?
                GROUP BY date
            )";

        var nextHourParams = parameters.Take(parameters.Count - 2).Concat(new object[] { nextHour, clickHouseDayOfWeek }).ToArray();
        var nextHourActivity = await ReadSingleOrDefaultAsync<ActivityMetrics>(nextHourQuery, nextHourParams) 
            ?? new ActivityMetrics();

        return new TrendInsights
        {
            CurrentHourAvgPlayers = currentActivity.AvgCurrentPlayers,
            CurrentHourAvgRounds = currentActivity.AvgCurrentRounds,
            NextHourAvgPlayers = nextHourActivity.AvgCurrentPlayers,
            NextHourAvgRounds = nextHourActivity.AvgCurrentRounds,
            TrendDirection = nextHourActivity.AvgCurrentPlayers > currentActivity.AvgCurrentPlayers ? "increasing" : "decreasing",
            PlayerTimeZoneOffsetHours = timeZoneOffsetHours,
            GeneratedAt = DateTime.UtcNow
        };
    }
}

// Data models for trend analysis
public class HourlyActivityTrend
{
    public int HourOfDay { get; set; }
    public int DayOfWeek { get; set; }  // 1=Monday, 7=Sunday (ClickHouse format)
    public int UniquePlayers { get; set; }
    public int TotalRounds { get; set; }
    public double AvgRoundDuration { get; set; }
    public int ActiveServers { get; set; }
    public int UniqueMaps { get; set; }
}

public class ServerActivityTrend
{
    public string ServerGuid { get; set; } = "";
    public int HourOfDay { get; set; }
    public int UniquePlayers { get; set; }
    public int TotalRounds { get; set; }
    public double AvgRoundDuration { get; set; }
    public int UniqueMaps { get; set; }
    public DateTime LastActivity { get; set; }
}

public class CurrentActivityStatus
{
    public string GameId { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public int ActiveRounds { get; set; }
    public DateTime LatestActivity { get; set; }
    public int MapsInRotation { get; set; }
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

public class GameModeActivityTrend
{
    public string MapName { get; set; } = "";
    public string GameId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public int HourOfDay { get; set; }
    public int UniquePlayers { get; set; }
    public int TotalRounds { get; set; }
    public double AvgRoundDuration { get; set; }
    public int ServersHosting { get; set; }
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