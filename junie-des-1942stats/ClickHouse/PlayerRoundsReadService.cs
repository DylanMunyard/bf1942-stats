using System.Net.Http;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class PlayerRoundsReadService : BaseClickHouseService, IClickHouseReader
{
    private readonly ILogger<PlayerRoundsReadService> _logger;

    public PlayerRoundsReadService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerRoundsReadService> logger) 
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
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
                    MinutesPlayed = int.Parse(parts[1]),
                    TotalKills = int.Parse(parts[2]),
                    TotalDeaths = int.Parse(parts[3])
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
                    Score = int.Parse(parts[1]),
                    Kills = int.Parse(parts[2]),
                    Deaths = int.Parse(parts[3]),
                    MapName = parts[4],
                    Timestamp = DateTime.Parse(parts[5]),
                    SessionId = parts[6].GetHashCode() // Use round_id hash as session ID substitute
                });
            }
        }

        return topScores;
    }

}