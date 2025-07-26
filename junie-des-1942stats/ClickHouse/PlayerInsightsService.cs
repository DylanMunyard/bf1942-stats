using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.PlayerStats.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ClickHouse;

public class PlayerInsightsService : BaseClickHouseService, IClickHouseReader
{
    private readonly ILogger<PlayerInsightsService> _logger;

    public PlayerInsightsService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerInsightsService> logger) 
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteQueryAsync(string query)
    {
        return await ExecuteQueryInternalAsync(query);
    }

    /// <summary>
    /// Get kill milestones for a player (5k, 10k, 20k, 50k kills)
    /// </summary>
    public async Task<List<KillMilestone>> GetPlayerKillMilestonesAsync(string playerName)
    {
        var query = $@"
WITH PlayerRoundsCumulative AS (
    SELECT 
        player_name,
        round_end_time,
        final_kills,
        SUM(final_kills) OVER (PARTITION BY player_name ORDER BY round_end_time ROWS UNBOUNDED PRECEDING) as cumulative_kills,
        rowNumberInAllBlocks() as row_num
    FROM player_rounds
    WHERE player_name = '{playerName.Replace("'", "''")}'
    ORDER BY round_end_time
),
PlayerRoundsWithPrevious AS (
    SELECT 
        p1.player_name,
        p1.round_end_time,
        p1.cumulative_kills,
        COALESCE(p2.cumulative_kills, 0) as previous_cumulative_kills
    FROM PlayerRoundsCumulative p1
    LEFT JOIN PlayerRoundsCumulative p2 ON p1.row_num = p2.row_num + 1
),
MilestoneRounds AS (
    SELECT 
        player_name,
        round_end_time,
        cumulative_kills,
        CASE 
            WHEN cumulative_kills >= 5000 AND previous_cumulative_kills < 5000 THEN 5000
            WHEN cumulative_kills >= 10000 AND previous_cumulative_kills < 10000 THEN 10000
            WHEN cumulative_kills >= 20000 AND previous_cumulative_kills < 20000 THEN 20000
            WHEN cumulative_kills >= 50000 AND previous_cumulative_kills < 50000 THEN 50000
            ELSE 0
        END as milestone
    FROM PlayerRoundsWithPrevious
)
SELECT 
    milestone,
    round_end_time as achieved_date,
    cumulative_kills as total_kills_at_milestone
FROM MilestoneRounds 
WHERE milestone > 0
ORDER BY milestone
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var milestones = new List<KillMilestone>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                milestones.Add(new KillMilestone
                {
                    Milestone = int.Parse(parts[0]),
                    AchievedDate = DateTime.Parse(parts[1]),
                    TotalKillsAtMilestone = int.Parse(parts[2])
                });
            }
        }

        // Calculate time to reach each milestone
        var firstRoundQuery = $@"
SELECT MIN(round_end_time) as first_round
FROM player_rounds
WHERE player_name = '{playerName.Replace("'", "''")}'
FORMAT TabSeparated";

        var firstRoundResult = await ExecuteQueryAsync(firstRoundQuery);
        DateTime? firstRound = null;
        
        if (!string.IsNullOrEmpty(firstRoundResult))
        {
            var firstRoundLine = firstRoundResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstRoundLine) && DateTime.TryParse(firstRoundLine, out var parsed))
            {
                firstRound = parsed;
            }
        }

        // Calculate days to reach each milestone
        if (firstRound.HasValue)
        {
            foreach (var milestone in milestones)
            {
                milestone.DaysToAchieve = (int)(milestone.AchievedDate - firstRound.Value).TotalDays;
            }
        }

        return milestones;
    }

    /// <summary>
    /// Get server-specific insights for a player (servers with 10+ hours)
    /// </summary>
    public async Task<List<ServerInsight>> GetPlayerServerInsightsAsync(string playerName)
    {
        var query = $@"
SELECT 
    server_guid,
    SUM(play_time_minutes) as total_minutes,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    MAX(final_score) as highest_score,
    argMax(round_id, final_score) as highest_score_round_id,
    argMax(map_name, final_score) as highest_score_map_name,
    argMax(round_start_time, final_score) as highest_score_start_time,
    COUNT(*) as total_rounds
FROM player_rounds
WHERE player_name = '{playerName.Replace("'", "''")}'
GROUP BY server_guid
HAVING total_minutes >= 600  -- 10 hours = 600 minutes
ORDER BY total_minutes DESC
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var insights = new List<ServerInsight>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 9)
            {
                var totalMinutes = double.Parse(parts[1]);
                var totalKills = int.Parse(parts[2]);
                var killsPerMinute = totalMinutes > 0 ? Math.Round(totalKills / totalMinutes, 3) : 0;

                insights.Add(new ServerInsight
                {
                    ServerGuid = parts[0],
                    TotalMinutes = totalMinutes,
                    TotalKills = totalKills,
                    TotalDeaths = int.Parse(parts[3]),
                    HighestScore = int.Parse(parts[4]),
                    HighestScoreSessionId = parts[5],
                    HighestScoreMapName = parts[6],
                    HighestScoreStartTime = DateTime.Parse(parts[7]),
                    KillsPerMinute = killsPerMinute,
                    TotalRounds = int.Parse(parts[8])
                });
            }
        }

        // Get server names
        if (insights.Any())
        {
            var serverGuids = string.Join("','", insights.Select(i => i.ServerGuid.Replace("'", "''")));
            var nameQuery = $@"
SELECT DISTINCT server_guid, any(game_id) as game_id
FROM player_rounds
WHERE server_guid IN ('{serverGuids}')
GROUP BY server_guid
FORMAT TabSeparated";

            var nameResult = await ExecuteQueryAsync(nameQuery);
            var serverInfo = new Dictionary<string, string>();

            foreach (var line in nameResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    serverInfo[parts[0]] = parts[1];
                }
            }

            foreach (var insight in insights)
            {
                if (serverInfo.TryGetValue(insight.ServerGuid, out var gameId))
                {
                    insight.GameId = gameId;
                }
            }
        }

        return insights;
    }
}