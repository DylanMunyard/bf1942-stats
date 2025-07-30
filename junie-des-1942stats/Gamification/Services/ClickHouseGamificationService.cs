using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace junie_des_1942stats.Gamification.Services;

public class ClickHouseGamificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _clickHouseUrl;
    private readonly ILogger<ClickHouseGamificationService> _logger;

    public ClickHouseGamificationService(
        IHttpClientFactory httpClientFactory, 
        string clickHouseUrl,
        ILogger<ClickHouseGamificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _clickHouseUrl = clickHouseUrl;
        _logger = logger;
    }

    // Achievement Operations
    public async Task InsertAchievementAsync(Achievement achievement)
    {
        var query = @"
            INSERT INTO player_achievements 
            (player_name, achievement_type, achievement_id, achievement_name, tier, 
             value, achieved_at, processed_at, server_guid, map_name, round_id, metadata)
            VALUES";

        var values = $@"(
            '{EscapeString(achievement.PlayerName)}',
            '{EscapeString(achievement.AchievementType)}',
            '{EscapeString(achievement.AchievementId)}',
            '{EscapeString(achievement.AchievementName)}',
            '{EscapeString(achievement.Tier)}',
            {achievement.Value},
            '{achievement.AchievedAt:yyyy-MM-dd HH:mm:ss}',
            '{achievement.ProcessedAt:yyyy-MM-dd HH:mm:ss}',
            '{EscapeString(achievement.ServerGuid)}',
            '{EscapeString(achievement.MapName)}',
            '{EscapeString(achievement.RoundId)}',
            '{EscapeString(achievement.Metadata)}'
        )";

        await ExecuteQueryAsync($"{query} {values}");
    }

    public async Task InsertAchievementsBatchAsync(List<Achievement> achievements)
    {
        if (!achievements.Any()) return;

        var query = @"
            INSERT INTO player_achievements 
            (player_name, achievement_type, achievement_id, achievement_name, tier, 
             value, achieved_at, processed_at, server_guid, map_name, round_id, metadata)
            VALUES";

        var valuesList = achievements.Select(achievement => $@"(
            '{EscapeString(achievement.PlayerName)}',
            '{EscapeString(achievement.AchievementType)}',
            '{EscapeString(achievement.AchievementId)}',
            '{EscapeString(achievement.AchievementName)}',
            '{EscapeString(achievement.Tier)}',
            {achievement.Value},
            '{achievement.AchievedAt:yyyy-MM-dd HH:mm:ss}',
            '{achievement.ProcessedAt:yyyy-MM-dd HH:mm:ss}',
            '{EscapeString(achievement.ServerGuid)}',
            '{EscapeString(achievement.MapName)}',
            '{EscapeString(achievement.RoundId)}',
            '{EscapeString(achievement.Metadata)}'
        )");

        var fullQuery = $"{query} {string.Join(",", valuesList)}";
        await ExecuteQueryAsync(fullQuery);
    }

    public async Task<DateTime> GetLastProcessedTimestampAsync()
    {
        var query = "SELECT MAX(processed_at) as last_processed FROM player_achievements";
        
        try
        {
            var result = await QueryAsync(query);
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length > 0 && DateTime.TryParse(lines[0], out var lastProcessed))
            {
                return lastProcessed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last processed timestamp, returning minimum date");
        }

        return DateTime.MinValue;
    }

    public async Task<List<Achievement>> GetPlayerAchievementsAsync(string playerName, int limit = 50)
    {
        var query = $@"
            SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                   value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
            FROM player_achievements
            WHERE player_name = '{EscapeString(playerName)}'
            ORDER BY achieved_at DESC
            LIMIT {limit}";

        var result = await QueryAsync(query);
        return ParseAchievements(result);
    }

    public async Task<List<Achievement>> GetPlayerAchievementsByTypeAsync(string playerName, string achievementType)
    {
        var query = $@"
            SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                   value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
            FROM player_achievements
            WHERE player_name = '{EscapeString(playerName)}'
            AND achievement_type = '{EscapeString(achievementType)}'
            ORDER BY achieved_at DESC";

        var result = await QueryAsync(query);
        return ParseAchievements(result);
    }

    public async Task<bool> PlayerHasAchievementAsync(string playerName, string achievementId)
    {
        var query = $@"
            SELECT COUNT(*) as count
            FROM player_achievements
            WHERE player_name = '{EscapeString(playerName)}'
            AND achievement_id = '{EscapeString(achievementId)}'";

        var result = await QueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        return lines.Length > 0 && int.TryParse(lines[0], out var count) && count > 0;
    }

    // Player Statistics Operations
    public async Task<PlayerGameStats?> GetPlayerTotalStatsAsync(string playerName)
    {
        var query = $@"
            SELECT 
                player_name,
                SUM(kills) as total_kills,
                SUM(deaths) as total_deaths,
                SUM(score) as total_score,
                SUM(play_time_minutes) as total_playtime
            FROM player_rounds
            WHERE player_name = '{EscapeString(playerName)}'
            GROUP BY player_name";

        var result = await QueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0) return null;

        var parts = lines[0].Split('\t');
        if (parts.Length >= 5)
        {
            return new PlayerGameStats
            {
                PlayerName = parts[0],
                TotalKills = int.Parse(parts[1]),
                TotalDeaths = int.Parse(parts[2]),
                TotalScore = int.Parse(parts[3]),
                TotalPlayTimeMinutes = int.Parse(parts[4]),
                LastUpdated = DateTime.UtcNow
            };
        }

        return null;
    }

    public async Task<PlayerGameStats?> GetPlayerStatsBeforeTimestampAsync(string playerName, DateTime beforeTime)
    {
        var query = $@"
            SELECT 
                player_name,
                SUM(kills) as total_kills,
                SUM(deaths) as total_deaths,
                SUM(score) as total_score,
                SUM(play_time_minutes) as total_playtime
            FROM player_rounds
            WHERE player_name = '{EscapeString(playerName)}'
            AND round_end_time < '{beforeTime:yyyy-MM-dd HH:mm:ss}'
            GROUP BY player_name";

        var result = await QueryAsync(query);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0) 
        {
            return new PlayerGameStats { PlayerName = playerName };
        }

        var parts = lines[0].Split('\t');
        if (parts.Length >= 5)
        {
            return new PlayerGameStats
            {
                PlayerName = parts[0],
                TotalKills = int.Parse(parts[1]),
                TotalDeaths = int.Parse(parts[2]),
                TotalScore = int.Parse(parts[3]),
                TotalPlayTimeMinutes = int.Parse(parts[4]),
                LastUpdated = DateTime.UtcNow
            };
        }

        return new PlayerGameStats { PlayerName = playerName };
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsSinceAsync(DateTime sinceTime)
    {
        var query = $@"
            SELECT player_name, round_id, server_guid, map_name, kills, deaths, score, 
                   play_time_minutes, round_end_time
            FROM player_rounds
            WHERE round_end_time >= '{sinceTime:yyyy-MM-dd HH:mm:ss}'
            ORDER BY round_end_time ASC";

        var result = await QueryAsync(query);
        return ParsePlayerRounds(result);
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsInPeriodAsync(DateTime startTime, DateTime endTime)
    {
        var query = $@"
            SELECT player_name, round_id, server_guid, map_name, kills, deaths, score, 
                   play_time_minutes, round_end_time
            FROM player_rounds
            WHERE round_end_time >= '{startTime:yyyy-MM-dd HH:mm:ss}'
            AND round_end_time <= '{endTime:yyyy-MM-dd HH:mm:ss}'
            ORDER BY round_end_time ASC";

        var result = await QueryAsync(query);
        return ParsePlayerRounds(result);
    }

    public async Task<List<PlayerRound>> GetPlayerRecentRoundsAsync(string playerName, int roundCount)
    {
        var query = $@"
            SELECT player_name, round_id, server_guid, map_name, kills, deaths, score, 
                   play_time_minutes, round_end_time
            FROM player_rounds
            WHERE player_name = '{EscapeString(playerName)}'
            ORDER BY round_end_time DESC
            LIMIT {roundCount}";

        var result = await QueryAsync(query);
        return ParsePlayerRounds(result);
    }

    // Leaderboard Operations
    public async Task<List<LeaderboardEntry>> GetKillStreakLeaderboardAsync(int limit = 100)
    {
        var query = $@"
            SELECT player_name, MAX(value) as best_streak, COUNT(*) as streak_count
            FROM player_achievements
            WHERE achievement_type = 'kill_streak'
            GROUP BY player_name
            ORDER BY best_streak DESC, streak_count DESC
            LIMIT {limit}";

        var result = await QueryAsync(query);
        var entries = new List<LeaderboardEntry>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        int rank = 1;
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                entries.Add(new LeaderboardEntry
                {
                    Rank = rank++,
                    PlayerName = parts[0],
                    Value = int.Parse(parts[1]),
                    DisplayValue = $"{parts[1]} kill streak",
                    AchievementCount = int.Parse(parts[2])
                });
            }
        }

        return entries;
    }

    // Helper Methods
    private async Task<string> QueryAsync(string query)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.PostAsync($"{_clickHouseUrl}:8123", 
            new StringContent(query, System.Text.Encoding.UTF8, "text/plain"));
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task ExecuteQueryAsync(string query)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.PostAsync($"{_clickHouseUrl}:8123", 
            new StringContent(query, System.Text.Encoding.UTF8, "text/plain"));
        
        response.EnsureSuccessStatusCode();
    }

    private string EscapeString(string input)
    {
        return input?.Replace("'", "''") ?? "";
    }

    private List<Achievement> ParseAchievements(string result)
    {
        var achievements = new List<Achievement>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 12)
            {
                achievements.Add(new Achievement
                {
                    PlayerName = parts[0],
                    AchievementType = parts[1],
                    AchievementId = parts[2],
                    AchievementName = parts[3],
                    Tier = parts[4],
                    Value = uint.Parse(parts[5]),
                    AchievedAt = DateTime.Parse(parts[6]),
                    ProcessedAt = DateTime.Parse(parts[7]),
                    ServerGuid = parts[8],
                    MapName = parts[9],
                    RoundId = parts[10],
                    Metadata = parts[11]
                });
            }
        }

        return achievements;
    }

    private List<PlayerRound> ParsePlayerRounds(string result)
    {
        var rounds = new List<PlayerRound>();
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 9)
            {
                rounds.Add(new PlayerRound
                {
                    PlayerName = parts[0],
                    RoundId = parts[1],
                    ServerGuid = parts[2],
                    MapName = parts[3],
                    FinalKills = uint.Parse(parts[4]),
                    FinalDeaths = uint.Parse(parts[5]),
                    FinalScore = int.Parse(parts[6]),
                    PlayTimeMinutes = int.Parse(parts[7]),
                    RoundEndTime = DateTime.Parse(parts[8])
                });
            }
        }

        return rounds;
    }
} 