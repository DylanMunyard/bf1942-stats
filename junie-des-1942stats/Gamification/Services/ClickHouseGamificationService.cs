using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using junie_des_1942stats.ClickHouse.Base;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace junie_des_1942stats.Gamification.Services;

public class ClickHouseGamificationService : BaseClickHouseService
{
    private readonly ILogger<ClickHouseGamificationService> _logger;

    public ClickHouseGamificationService(
        HttpClient httpClient, 
        string clickHouseUrl,
        ILogger<ClickHouseGamificationService> logger)
        : base(httpClient, clickHouseUrl)
    {
        _logger = logger;
    }

    // Achievement Operations

    public async Task InsertAchievementsBatchAsync(List<Achievement> achievements)
    {
        if (!achievements.Any()) return;

        const int batchSize = 10_000;
        for (int i = 0; i < achievements.Count; i += batchSize)
        {
            var batch = achievements.Skip(i).Take(batchSize).ToList();
            await InsertAchievementsBatchInternalAsync(batch);
        }
    }

    private async Task InsertAchievementsBatchInternalAsync(List<Achievement> achievements)
    {
        try
        {
            using var stringWriter = new StringWriter();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            csvWriter.WriteRecords(achievements.Select(a => new
            {
                PlayerName = a.PlayerName,
                AchievementType = a.AchievementType,
                AchievementId = a.AchievementId,
                AchievementName = a.AchievementName,
                Tier = a.Tier,
                Value = a.Value,
                AchievedAt = a.AchievedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ProcessedAt = a.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ServerGuid = a.ServerGuid,
                MapName = a.MapName,
                RoundId = a.RoundId,
                Metadata = a.Metadata
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_achievements (player_name, achievement_type, achievement_id, achievement_name, tier, value, achieved_at, processed_at, server_guid, map_name, round_id, metadata) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            await ExecuteQueryAsync(fullRequest);
            _logger.LogInformation("Successfully inserted {Count} achievements to ClickHouse", achievements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert achievements to ClickHouse");
            throw;
        }
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

    public async Task<(List<Achievement> Achievements, int TotalCount)> GetAllAchievementsWithPagingAsync(
        int page, 
        int pageSize, 
        string sortBy = "AchievedAt", 
        string sortOrder = "desc",
        string? playerName = null,
        string? achievementType = null,
        string? achievementId = null,
        string? tier = null,
        DateTime? achievedFrom = null,
        DateTime? achievedTo = null,
        string? serverGuid = null,
        string? mapName = null)
    {
        // Build WHERE clause
        var whereConditions = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(playerName))
            whereConditions.Add($"player_name = '{EscapeString(playerName)}'");
        
        if (!string.IsNullOrWhiteSpace(achievementType))
            whereConditions.Add($"achievement_type = '{EscapeString(achievementType)}'");
        
        if (!string.IsNullOrWhiteSpace(achievementId))
            whereConditions.Add($"achievement_id = '{EscapeString(achievementId)}'");
        
        if (!string.IsNullOrWhiteSpace(tier))
            whereConditions.Add($"tier = '{EscapeString(tier)}'");
        
        if (achievedFrom.HasValue)
            whereConditions.Add($"achieved_at >= '{achievedFrom.Value:yyyy-MM-dd HH:mm:ss}'");
        
        if (achievedTo.HasValue)
            whereConditions.Add($"achieved_at <= '{achievedTo.Value:yyyy-MM-dd HH:mm:ss}'");
        
        if (!string.IsNullOrWhiteSpace(serverGuid))
            whereConditions.Add($"server_guid = '{EscapeString(serverGuid)}'");
        
        if (!string.IsNullOrWhiteSpace(mapName))
            whereConditions.Add($"map_name = '{EscapeString(mapName)}'");

        var whereClause = whereConditions.Any() ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

        // Validate and map sort field
        var validSortFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "PlayerName", "player_name" },
            { "AchievementType", "achievement_type" },
            { "AchievementId", "achievement_id" },
            { "AchievementName", "achievement_name" },
            { "Tier", "tier" },
            { "Value", "value" },
            { "AchievedAt", "achieved_at" },
            { "ProcessedAt", "processed_at" },
            { "ServerGuid", "server_guid" },
            { "MapName", "map_name" }
        };

        if (!validSortFields.ContainsKey(sortBy))
            sortBy = "AchievedAt";

        var sortField = validSortFields[sortBy];
        var orderDirection = sortOrder.ToLower() == "asc" ? "ASC" : "DESC";

        // Get total count
        var countQuery = $@"
            SELECT COUNT(*) as total
            FROM player_achievements
            {whereClause}";

        var countResult = await QueryAsync(countQuery);
        var countLines = countResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var totalCount = countLines.Length > 0 && int.TryParse(countLines[0], out var count) ? count : 0;

        // Calculate offset
        var offset = (page - 1) * pageSize;

        // Get achievements with pagination
        var query = $@"
            SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                   value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
            FROM player_achievements
            {whereClause}
            ORDER BY {sortField} {orderDirection}
            LIMIT {pageSize} OFFSET {offset}";

        var result = await QueryAsync(query);
        var achievements = ParseAchievements(result);

        return (achievements, totalCount);
    }

    // Player Statistics Operations
    public async Task<PlayerGameStats?> GetPlayerTotalStatsAsync(string playerName)
    {
        var query = $@"
            SELECT 
                player_name,
                SUM(final_kills) as total_kills,
                SUM(final_deaths) as total_deaths,
                SUM(final_score) as total_score,
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
                TotalPlayTimeMinutes = (int)Math.Round(double.Parse(parts[4], CultureInfo.InvariantCulture)),
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
                SUM(final_kills) as total_kills,
                SUM(final_deaths) as total_deaths,
                SUM(final_score) as total_score,
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
                TotalPlayTimeMinutes = (int)Math.Round(double.Parse(parts[4], CultureInfo.InvariantCulture)),
                LastUpdated = DateTime.UtcNow
            };
        }

        return new PlayerGameStats { PlayerName = playerName };
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsSinceAsync(DateTime sinceTime)
    {
        var query = $@"
            SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
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
            SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
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
            SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
                   play_time_minutes, round_end_time
            FROM player_rounds
            WHERE player_name = '{EscapeString(playerName)}'
            ORDER BY round_end_time DESC
            LIMIT {roundCount}";

        var result = await QueryAsync(query);
        return ParsePlayerRounds(result);
    }

    // PlayerRound Bulk Operations
    public async Task InsertPlayerRoundsBatchAsync(List<PlayerRound> playerRounds)
    {
        if (!playerRounds.Any()) return;

        const int batchSize = 1000;
        for (int i = 0; i < playerRounds.Count; i += batchSize)
        {
            var batch = playerRounds.Skip(i).Take(batchSize).ToList();
            await InsertPlayerRoundsBatchInternalAsync(batch);
        }
    }

    private async Task InsertPlayerRoundsBatchInternalAsync(List<PlayerRound> playerRounds)
    {
        try
        {
            using var stringWriter = new StringWriter();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            csvWriter.WriteRecords(playerRounds.Select(r => new
            {
                PlayerName = r.PlayerName,
                RoundId = r.RoundId,
                ServerGuid = r.ServerGuid,
                MapName = r.MapName,
                FinalKills = r.FinalKills,
                FinalDeaths = r.FinalDeaths,
                FinalScore = r.FinalScore,
                PlayTimeMinutes = r.PlayTimeMinutes.ToString("F2", CultureInfo.InvariantCulture),
                RoundEndTime = r.RoundEndTime.ToString("yyyy-MM-dd HH:mm:ss")
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_rounds (player_name, round_id, server_guid, map_name, final_kills, final_deaths, final_score, play_time_minutes, round_end_time) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            await ExecuteQueryAsync(fullRequest);
            _logger.LogInformation("Successfully inserted {Count} player rounds to ClickHouse", playerRounds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert player rounds to ClickHouse");
            throw;
        }
    }

    // PlayerGameStats Bulk Operations
    public async Task InsertPlayerStatsBatchAsync(List<PlayerGameStats> playerStats)
    {
        if (!playerStats.Any()) return;

        const int batchSize = 1000;
        for (int i = 0; i < playerStats.Count; i += batchSize)
        {
            var batch = playerStats.Skip(i).Take(batchSize).ToList();
            await InsertPlayerStatsBatchInternalAsync(batch);
        }
    }

    private async Task InsertPlayerStatsBatchInternalAsync(List<PlayerGameStats> playerStats)
    {
        try
        {
            using var stringWriter = new StringWriter();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csvWriter = new CsvWriter(stringWriter, config);

            csvWriter.WriteRecords(playerStats.Select(s => new
            {
                PlayerName = s.PlayerName,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                TotalScore = s.TotalScore,
                TotalPlayTimeMinutes = s.TotalPlayTimeMinutes,
                LastUpdated = s.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_stats (player_name, total_kills, total_deaths, total_score, total_playtime_minutes, last_updated) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            await ExecuteQueryAsync(fullRequest);
            _logger.LogInformation("Successfully inserted {Count} player stats to ClickHouse", playerStats.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert player stats to ClickHouse");
            throw;
        }
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
        return await ExecuteQueryInternalAsync(query);
    }

    private async Task ExecuteQueryAsync(string query)
    {
        await ExecuteCommandInternalAsync(query);
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
                    PlayTimeMinutes = (int)Math.Round(double.Parse(parts[7])),
                    RoundEndTime = DateTime.Parse(parts[8])
                });
            }
        }

        return rounds;
    }
} 