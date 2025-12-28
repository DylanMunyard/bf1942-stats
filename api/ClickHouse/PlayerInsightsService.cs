using api.ClickHouse.Base;
using api.ClickHouse.Interfaces;
using api.ClickHouse.Models;
using api.Players.Models;
using Microsoft.Extensions.Logging;
using api.Telemetry;

namespace api.ClickHouse;

public class PlayerInsightsService(HttpClient httpClient, string clickHouseUrl, ILogger<PlayerInsightsService> logger) : BaseClickHouseService(httpClient, clickHouseUrl), IClickHouseReader, IPlayerInsightsService
{

    public async Task<string> ExecuteQueryAsync(string query)
    {
        return await ExecuteQueryInternalAsync(query);
    }

    /// <summary>
    /// Get kill milestones for one or more players (5k, 10k, 20k, 50k, 75k, 100k kills)
    /// </summary>
    public async Task<List<PlayerKillMilestone>> GetPlayersKillMilestonesAsync(List<string> playerNames)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("GetPlayersKillMilestones");
        activity?.SetTag("players.count", playerNames.Count);
        activity?.SetTag("players.names", string.Join(", ", playerNames.Take(5))); // Limit to first 5 for readability

        if (!playerNames.Any()) return new List<PlayerKillMilestone>();

        var playerNamesQuoted = string.Join(", ", playerNames.Select(p => $"'{p.Replace("'", "''")}'"));

        var query = $@"
WITH PlayerRoundsCumulative AS (
    SELECT 
        player_name,
        round_end_time,
        final_kills,
        SUM(final_kills) OVER (PARTITION BY player_name ORDER BY round_end_time ROWS UNBOUNDED PRECEDING) as cumulative_kills,
        row_number() OVER (PARTITION BY player_name ORDER BY round_end_time) as row_num
    FROM player_rounds
    WHERE player_name IN ({playerNamesQuoted})
    ORDER BY player_name, round_end_time
),
PlayerRoundsWithPrevious AS (
    SELECT 
        p1.player_name,
        p1.round_end_time,
        p1.cumulative_kills,
        COALESCE(p2.cumulative_kills, 0) as previous_cumulative_kills
    FROM PlayerRoundsCumulative p1
    LEFT JOIN PlayerRoundsCumulative p2 ON p1.player_name = p2.player_name AND p1.row_num = p2.row_num + 1
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
            WHEN cumulative_kills >= 75000 AND previous_cumulative_kills < 75000 THEN 75000
            WHEN cumulative_kills >= 100000 AND previous_cumulative_kills < 100000 THEN 100000
            ELSE 0
        END as milestone
    FROM PlayerRoundsWithPrevious
),
FirstRounds AS (
    SELECT 
        player_name,
        MIN(round_start_time) as first_round_time
    FROM player_rounds
    WHERE player_name IN ({playerNamesQuoted})
    GROUP BY player_name
)
SELECT 
    m.player_name,
    m.milestone,
    m.round_end_time as achieved_date,
    m.cumulative_kills as total_kills_at_milestone,
    dateDiff('day', f.first_round_time, m.round_end_time) as days_to_achieve
FROM MilestoneRounds m
JOIN FirstRounds f ON m.player_name = f.player_name
WHERE m.milestone > 0
ORDER BY m.player_name, m.milestone
FORMAT TabSeparated";

        var result = await ExecuteQueryAsync(query);
        var milestones = new List<PlayerKillMilestone>();

        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 5)
            {
                milestones.Add(new PlayerKillMilestone
                {
                    PlayerName = parts[0],
                    Milestone = int.TryParse(parts[1], out var milestone) ? milestone : 0,
                    AchievedDate = DateTime.TryParse(parts[2], out var achieved) ? achieved : DateTime.MinValue,
                    TotalKillsAtMilestone = int.TryParse(parts[3], out var killsMilestone) ? killsMilestone : 0,
                    DaysToAchieve = int.TryParse(parts[4], out var days) ? days : 0
                });
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
            if (parts.Length >= 7)
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
                    HighestScore = int.TryParse(parts[4], out var score) ? score : 0,
                    HighestScoreRoundId = parts[5],
                    KillsPerMinute = killsPerMinute,
                    TotalRounds = int.Parse(parts[6])
                });
            }
        }


        return insights;
    }

    /// <summary>
    /// Get kill milestones for a single player (convenience method)
    /// </summary>
    public async Task<List<KillMilestone>> GetPlayerKillMilestonesAsync(string playerName)
    {
        var playerMilestones = await GetPlayersKillMilestonesAsync(new List<string> { playerName });
        return playerMilestones.Select(m => new KillMilestone
        {
            Milestone = m.Milestone,
            AchievedDate = m.AchievedDate,
            TotalKillsAtMilestone = m.TotalKillsAtMilestone,
            DaysToAchieve = m.DaysToAchieve
        }).ToList();
    }
}
