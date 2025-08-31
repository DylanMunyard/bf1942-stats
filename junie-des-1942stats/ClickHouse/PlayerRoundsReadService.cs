using System.Net.Http;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ServerStats.Models;
using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.Telemetry;
using System.Diagnostics;

namespace junie_des_1942stats.ClickHouse;

public class PlayerRoundsReadService : BaseClickHouseService, IClickHouseReader
{
    private readonly ILogger<PlayerRoundsReadService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public PlayerRoundsReadService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerRoundsReadService> logger, IServiceProvider serviceProvider)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> ExecuteQueryAsync(string query)
    {
        return await ExecuteQueryInternalAsync(query);
    }

    /// <summary>
    /// Query aggregated player statistics from the player_rounds table
    /// </summary>
    public async Task<string> GetPlayerStatsAsync(string? playerName = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("GetPlayerStats");
        activity?.SetTag("player.name", playerName);
        activity?.SetTag("query.from_date", fromDate?.ToString("yyyy-MM-dd HH:mm:ss"));
        activity?.SetTag("query.to_date", toDate?.ToString("yyyy-MM-dd HH:mm:ss"));
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(playerName))
            conditions.Add($"player_name = '{playerName.Replace("'", "''")}'");

        if (fromDate.HasValue)
            conditions.Add($"round_start_time >= '{fromDate.Value:yyyy-MM-dd HH:mm:ss}'");

        if (toDate.HasValue)
            conditions.Add($"round_start_time <= '{toDate.Value:yyyy-MM-dd HH:mm:ss}'");

        var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";

        var query = $@"
SELECT 
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths, 
    SUM(play_time_minutes) as total_play_time_minutes,
    AVG(final_score) as avg_score_per_round,
    CASE WHEN SUM(final_deaths) > 0 THEN round(SUM(final_kills) / SUM(final_deaths), 3) ELSE toFloat64(SUM(final_kills)) END as kd_ratio
FROM player_rounds 
{whereClause}
GROUP BY player_name
HAVING total_kills > 10 
ORDER BY total_play_time_minutes DESC
FORMAT TabSeparatedWithNames";

        return await ExecuteQueryAsync(query);
    }

    /// <summary>
    /// Get most active players by time played from ClickHouse
    /// </summary>
    public async Task<List<PlayerActivity>> GetMostActivePlayersAsync(string serverGuid, DateTime startPeriod, DateTime endPeriod, int limit = 10)
    {
        var query = $@"
SELECT 
    player_name,
    CAST(SUM(play_time_minutes) AS INTEGER) as minutes_played,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
  AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
  AND round_end_time <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
GROUP BY player_name
ORDER BY minutes_played DESC
LIMIT {limit}
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var players = new List<PlayerActivity>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 4)
            {
                players.Add(new PlayerActivity
                {
                    PlayerName = parts[0],
                    MinutesPlayed = int.TryParse(parts[1], out var minutes) ? minutes : 0,
                    TotalKills = int.TryParse(parts[2], out var kills) ? kills : 0,
                    TotalDeaths = int.TryParse(parts[3], out var deaths) ? deaths : 0
                });
            }
        }

        return players;
    }

    /// <summary>
    /// Get top scores from ClickHouse
    /// </summary>
    public async Task<List<TopScore>> GetTopScoresAsync(string serverGuid, DateTime startPeriod, DateTime endPeriod, int limit = 10)
    {
        var query = $@"
SELECT 
    player_name,
    final_score,
    final_kills,
    final_deaths,
    map_name,
    round_end_time,
    round_id
FROM player_rounds
WHERE server_guid = '{serverGuid.Replace("'", "''")}'
  AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
  AND round_end_time <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
  AND is_bot = 0
ORDER BY final_score DESC
LIMIT {limit}
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var topScores = new List<TopScore>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 7)
            {
                topScores.Add(new TopScore
                {
                    PlayerName = parts[0],
                    Score = int.TryParse(parts[1], out var score) ? score : 0,
                    Kills = int.TryParse(parts[2], out var kills) ? kills : 0,
                    Deaths = int.TryParse(parts[3], out var deaths) ? deaths : 0,
                    MapName = parts[4],
                    Timestamp = DateTime.TryParse(parts[5], out var date) ? date : DateTime.MinValue,
                    SessionId = parts[6].GetHashCode() // Use round_id hash as session ID substitute
                });
            }
        }

        return topScores;
    }


    /// <summary>
    /// Get player's time series trend data for K/D ratio and kill rate over the specified time period
    /// Aggregates data by day to provide good granularity for trend analysis
    /// </summary>
    public async Task<string> GetPlayerTimeSeriesTrendAsync(string playerName, DateTime fromDate)
    {
        var query = $@"
WITH player_rounds_daily AS (
    SELECT 
        player_name,
        toDate(round_end_time) as day_date,
        SUM(final_kills) as daily_kills,
        SUM(final_deaths) as daily_deaths,
        SUM(play_time_minutes) as daily_minutes
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_end_time >= '{fromDate:yyyy-MM-dd HH:mm:ss}'
    GROUP BY player_name, toDate(round_end_time)
    HAVING daily_kills > 0 OR daily_deaths > 0
),
cumulative_daily AS (
    SELECT 
        day_date,
        daily_kills,
        daily_deaths,
        daily_minutes,
        -- Running totals for cumulative K/D and kill rate trends
        SUM(daily_kills) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_kills,
        SUM(daily_deaths) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_deaths,
        SUM(daily_minutes) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_minutes
    FROM player_rounds_daily
)
SELECT 
    day_date as timestamp,
    CASE WHEN cumulative_deaths > 0 THEN round(cumulative_kills / cumulative_deaths, 3) ELSE toFloat64(cumulative_kills) END as kd_ratio,
    CASE WHEN cumulative_minutes > 0 THEN round(cumulative_kills / cumulative_minutes, 3) ELSE 0.0 END as kill_rate
FROM cumulative_daily
ORDER BY day_date
FORMAT TabSeparatedWithNames";

        return await ExecuteQueryAsync(query);
    }

    /// <summary>
    /// Get player's best scores for different time periods: this week, last 30 days, and all time
    /// </summary>
    public async Task<PlayerBestScores> GetPlayerBestScoresAsync(string playerName)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("GetPlayerBestScores");
        activity?.SetTag("player.name", playerName);
        var thisWeekStart = DateTime.UtcNow.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
        var last30DaysStart = DateTime.UtcNow.AddDays(-30);

        var query = $@"
SELECT 
    'ThisWeek' as period,
    final_score,
    final_kills,
    final_deaths,
    map_name,
    server_guid,
    round_end_time,
    round_id
FROM (
    SELECT 
        final_score,
        final_kills,
        final_deaths,
        map_name,
        server_guid,
        round_end_time,
        round_id
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND final_score > 0
      AND round_end_time >= '{thisWeekStart:yyyy-MM-dd HH:mm:ss}'
    ORDER BY final_score DESC
    LIMIT 3
)

UNION ALL

SELECT 
    'Last30Days' as period,
    final_score,
    final_kills,
    final_deaths,
    map_name,
    server_guid,
    round_end_time,
    round_id
FROM (
    SELECT 
        final_score,
        final_kills,
        final_deaths,
        map_name,
        server_guid,
        round_end_time,
        round_id
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND final_score > 0
      AND round_end_time >= '{last30DaysStart:yyyy-MM-dd HH:mm:ss}'
    ORDER BY final_score DESC
    LIMIT 3
)

UNION ALL

SELECT 
    'AllTime' as period,
    final_score,
    final_kills,
    final_deaths,
    map_name,
    server_guid,
    round_end_time,
    round_id
FROM (
    SELECT 
        final_score,
        final_kills,
        final_deaths,
        map_name,
        server_guid,
        round_end_time,
        round_id
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND final_score > 0
    ORDER BY final_score DESC
    LIMIT 3
)
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var bestScores = ParseBestScoresResult(result);

        // Replace server GUIDs with server names
        await ReplaceServerGuidsWithNamesAsync(bestScores);

        return bestScores;
    }

    private PlayerBestScores ParseBestScoresResult(string result)
    {
        var bestScores = new PlayerBestScores();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 8)
            {
                var scoreDetail = new BestScoreDetail
                {
                    Score = int.TryParse(parts[1], out var score) ? score : 0,
                    Kills = int.TryParse(parts[2], out var kills) ? kills : 0,
                    Deaths = int.TryParse(parts[3], out var deaths) ? deaths : 0,
                    MapName = parts[4],
                    ServerGuid = parts[5], // Store the server GUID
                    Timestamp = DateTime.TryParse(parts[6], out var date) ? date : DateTime.MinValue,
                    RoundId = parts[7]
                };

                switch (parts[0])
                {
                    case "ThisWeek":
                        bestScores.ThisWeek.Add(scoreDetail);
                        break;
                    case "Last30Days":
                        bestScores.Last30Days.Add(scoreDetail);
                        break;
                    case "AllTime":
                        bestScores.AllTime.Add(scoreDetail);
                        break;
                }
            }
        }

        return bestScores;
    }

    private async Task ReplaceServerGuidsWithNamesAsync(PlayerBestScores bestScores)
    {
        // Collect all unique server GUIDs from all time periods
        var allScoreDetails = new List<BestScoreDetail>();
        allScoreDetails.AddRange(bestScores.ThisWeek);
        allScoreDetails.AddRange(bestScores.Last30Days);
        allScoreDetails.AddRange(bestScores.AllTime);

        if (!allScoreDetails.Any())
            return;

        var serverGuids = allScoreDetails
            .Select(s => s.ServerGuid) // Use the ServerGuid field
            .Where(guid => !string.IsNullOrEmpty(guid))
            .Distinct()
            .ToList();

        if (!serverGuids.Any())
            return;

        // Look up server names from the database
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        var serverLookup = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        // Replace server names with actual server names (keep GUIDs unchanged)
        foreach (var scoreDetail in allScoreDetails)
        {
            scoreDetail.ServerName =
                serverLookup.TryGetValue(scoreDetail.ServerGuid, out var serverName) ? serverName : "";
        }
    }

}