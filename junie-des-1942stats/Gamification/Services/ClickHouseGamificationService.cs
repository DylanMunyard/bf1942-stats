using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ClickHouse.Client.ADO;

namespace junie_des_1942stats.Gamification.Services;

public class ClickHouseGamificationService : IDisposable
{
    private readonly ILogger<ClickHouseGamificationService> _logger;
    private readonly ClickHouseConnection _connection;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public ClickHouseGamificationService(
        IConfiguration configuration,
        ILogger<ClickHouseGamificationService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        
        try
        {
            var uri = new Uri(clickHouseUrl);
            var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
            _connection = new ClickHouseConnection(connectionString);
            _logger.LogInformation("ClickHouse connection initialized with URL: {Url}", clickHouseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ClickHouse connection with URL: {Url}", clickHouseUrl);
            throw;
        }
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
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = "SELECT MAX(processed_at) as last_processed FROM player_achievements";
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            var result = await command.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value && DateTime.TryParse(result.ToString(), out var lastProcessed))
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
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
                FROM player_achievements
                WHERE player_name = {playerName:String}
                ORDER BY achieved_at DESC
                LIMIT {limit:UInt32}";

            var results = new List<Achievement>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("limit", limit));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new Achievement
                {
                    PlayerName = reader.GetString(0),
                    AchievementType = reader.GetString(1),
                    AchievementId = reader.GetString(2),
                    AchievementName = reader.GetString(3),
                    Tier = reader.GetString(4),
                    Value = Convert.ToUInt32(reader.GetValue(5)),
                    AchievedAt = reader.GetDateTime(6),
                    ProcessedAt = reader.GetDateTime(7),
                    ServerGuid = reader.GetString(8),
                    MapName = reader.GetString(9),
                    RoundId = reader.GetString(10),
                    Metadata = reader.GetString(11)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player achievements for {PlayerName}", playerName);
            throw;
        }
    }

    public async Task<List<Achievement>> GetPlayerAchievementsByTypeAsync(string playerName, string achievementType)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
                FROM player_achievements
                WHERE player_name = {playerName:String}
                AND achievement_type = {achievementType:String}
                ORDER BY achieved_at DESC";

            var results = new List<Achievement>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("achievementType", achievementType));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new Achievement
                {
                    PlayerName = reader.GetString(0),
                    AchievementType = reader.GetString(1),
                    AchievementId = reader.GetString(2),
                    AchievementName = reader.GetString(3),
                    Tier = reader.GetString(4),
                    Value = Convert.ToUInt32(reader.GetValue(5)),
                    AchievedAt = reader.GetDateTime(6),
                    ProcessedAt = reader.GetDateTime(7),
                    ServerGuid = reader.GetString(8),
                    MapName = reader.GetString(9),
                    RoundId = reader.GetString(10),
                    Metadata = reader.GetString(11)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player achievements by type for {PlayerName}, type {AchievementType}", playerName, achievementType);
            throw;
        }
    }

    public async Task<bool> PlayerHasAchievementAsync(string playerName, string achievementId)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT COUNT(*) as count
                FROM player_achievements
                WHERE player_name = {playerName:String}
                AND achievement_id = {achievementId:String}";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("achievementId", achievementId));
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if player {PlayerName} has achievement {AchievementId}", playerName, achievementId);
            throw;
        }
    }

    public async Task<List<string>> GetPlayerAchievementIdsAsync(string playerName)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT DISTINCT achievement_id
                FROM player_achievements
                WHERE player_name = {playerName:String}
                ORDER BY achievement_id";

            var results = new List<string>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get achievement IDs for player {PlayerName}", playerName);
            throw;
        }
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
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            // Build WHERE clause with parameters
            var whereConditions = new List<string>();
            var parameters = new List<System.Data.Common.DbParameter>();
            
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                whereConditions.Add("player_name = {playerName:String}");
                parameters.Add(CreateParameter("playerName", playerName));
            }
            
            if (!string.IsNullOrWhiteSpace(achievementType))
            {
                whereConditions.Add("achievement_type = {achievementType:String}");
                parameters.Add(CreateParameter("achievementType", achievementType));
            }
            
            if (!string.IsNullOrWhiteSpace(achievementId))
            {
                whereConditions.Add("achievement_id = {achievementId:String}");
                parameters.Add(CreateParameter("achievementId", achievementId));
            }
            
            if (!string.IsNullOrWhiteSpace(tier))
            {
                whereConditions.Add("tier = {tier:String}");
                parameters.Add(CreateParameter("tier", tier));
            }
            
            if (achievedFrom.HasValue)
            {
                whereConditions.Add("achieved_at >= {achievedFrom:DateTime}");
                parameters.Add(CreateParameter("achievedFrom", achievedFrom.Value));
            }
            
            if (achievedTo.HasValue)
            {
                whereConditions.Add("achieved_at <= {achievedTo:DateTime}");
                parameters.Add(CreateParameter("achievedTo", achievedTo.Value));
            }
            
            if (!string.IsNullOrWhiteSpace(serverGuid))
            {
                whereConditions.Add("server_guid = {serverGuid:String}");
                parameters.Add(CreateParameter("serverGuid", serverGuid));
            }
            
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                whereConditions.Add("map_name = {mapName:String}");
                parameters.Add(CreateParameter("mapName", mapName));
            }

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

            int totalCount;
            await using (var countCommand = _connection.CreateCommand())
            {
                countCommand.CommandText = countQuery;
                foreach (var param in parameters)
                {
                    countCommand.Parameters.Add(param);
                }
                
                var countResult = await countCommand.ExecuteScalarAsync();
                totalCount = Convert.ToInt32(countResult);
            }

            // Calculate offset
            var offset = (page - 1) * pageSize;

            // Get achievements with pagination
            var query = $@"
                SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata
                FROM player_achievements
                {whereClause}
                ORDER BY {sortField} {orderDirection}
                LIMIT {pageSize:UInt32} OFFSET {offset:UInt32}";

            var results = new List<Achievement>();
            await using (var command = _connection.CreateCommand())
            {
                command.CommandText = query;
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }
                command.Parameters.Add(CreateParameter("pageSize", pageSize));
                command.Parameters.Add(CreateParameter("offset", offset));
                
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new Achievement
                    {
                        PlayerName = reader.GetString(0),
                        AchievementType = reader.GetString(1),
                        AchievementId = reader.GetString(2),
                        AchievementName = reader.GetString(3),
                        Tier = reader.GetString(4),
                        Value = Convert.ToUInt32(reader.GetValue(5)),
                        AchievedAt = reader.GetDateTime(6),
                        ProcessedAt = reader.GetDateTime(7),
                        ServerGuid = reader.GetString(8),
                        MapName = reader.GetString(9),
                        RoundId = reader.GetString(10),
                        Metadata = reader.GetString(11)
                    });
                }
            }

            return (results, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get achievements with paging");
            throw;
        }
    }

    // Player Statistics Operations
    public async Task<PlayerGameStats?> GetPlayerTotalStatsAsync(string playerName)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT 
                    player_name,
                    SUM(final_kills) as total_kills,
                    SUM(final_deaths) as total_deaths,
                    SUM(final_score) as total_score,
                    SUM(play_time_minutes) as total_playtime
                FROM player_rounds
                WHERE player_name = {playerName:String}
                GROUP BY player_name";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("playerName", playerName));
            
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PlayerGameStats
                {
                    PlayerName = reader.GetString(0),
                    TotalKills = Convert.ToInt32(reader.GetValue(1)),
                    TotalDeaths = Convert.ToInt32(reader.GetValue(2)),
                    TotalScore = Convert.ToInt32(reader.GetValue(3)),
                    TotalPlayTimeMinutes = (int)Math.Round(Convert.ToDouble(reader.GetValue(4))),
                    LastUpdated = DateTime.UtcNow
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get total stats for player {PlayerName}", playerName);
            throw;
        }
    }

    public async Task<PlayerGameStats?> GetPlayerStatsBeforeTimestampAsync(string playerName, DateTime beforeTime)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT 
                    player_name,
                    SUM(final_kills) as total_kills,
                    SUM(final_deaths) as total_deaths,
                    SUM(final_score) as total_score,
                    SUM(play_time_minutes) as total_playtime
                FROM player_rounds
                WHERE player_name = {playerName:String}
                AND round_end_time < {beforeTime:DateTime}
                GROUP BY player_name";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("beforeTime", beforeTime));
            
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PlayerGameStats
                {
                    PlayerName = reader.GetString(0),
                    TotalKills = Convert.ToInt32(reader.GetValue(1)),
                    TotalDeaths = Convert.ToInt32(reader.GetValue(2)),
                    TotalScore = Convert.ToInt32(reader.GetValue(3)),
                    TotalPlayTimeMinutes = (int)Math.Round(Convert.ToDouble(reader.GetValue(4))),
                    LastUpdated = DateTime.UtcNow
                };
            }

            return new PlayerGameStats { PlayerName = playerName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player stats before timestamp for {PlayerName}", playerName);
            throw;
        }
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsSinceAsync(DateTime sinceTime)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
                       play_time_minutes, round_start_time, round_end_time
                FROM player_rounds
                WHERE round_end_time >= {sinceTime:DateTime}
                ORDER BY round_end_time ASC";

            var results = new List<PlayerRound>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("sinceTime", sinceTime));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new PlayerRound
                {
                    PlayerName = reader.GetString(0),
                    RoundId = reader.GetString(1),
                    ServerGuid = reader.GetString(2),
                    MapName = reader.GetString(3),
                    FinalKills = Convert.ToUInt32(reader.GetValue(4)),
                    FinalDeaths = Convert.ToUInt32(reader.GetValue(5)),
                    FinalScore = Convert.ToInt32(reader.GetValue(6)),
                    PlayTimeMinutes = (int)Math.Round(Convert.ToDouble(reader.GetValue(7))),
                    RoundStartTime = reader.GetDateTime(8),
                    RoundEndTime = reader.GetDateTime(9)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player rounds since {SinceTime}", sinceTime);
            throw;
        }
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsInPeriodAsync(DateTime startTime, DateTime endTime)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
                       play_time_minutes, round_end_time
                FROM player_rounds
                WHERE round_end_time >= {startTime:DateTime}
                AND round_end_time <= {endTime:DateTime}
                ORDER BY round_end_time ASC";

            var results = new List<PlayerRound>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("startTime", startTime));
            command.Parameters.Add(CreateParameter("endTime", endTime));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new PlayerRound
                {
                    PlayerName = reader.GetString(0),
                    RoundId = reader.GetString(1),
                    ServerGuid = reader.GetString(2),
                    MapName = reader.GetString(3),
                    FinalKills = Convert.ToUInt32(reader.GetValue(4)),
                    FinalDeaths = Convert.ToUInt32(reader.GetValue(5)),
                    FinalScore = Convert.ToInt32(reader.GetValue(6)),
                    PlayTimeMinutes = (int)Math.Round(Convert.ToDouble(reader.GetValue(7))),
                    RoundEndTime = reader.GetDateTime(8)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player rounds in period {StartTime} to {EndTime}", startTime, endTime);
            throw;
        }
    }

    public async Task<List<PlayerRound>> GetPlayerRecentRoundsAsync(string playerName, int roundCount)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, round_id, server_guid, map_name, final_kills as kills, final_deaths as deaths, final_score as score, 
                       play_time_minutes, round_end_time
                FROM player_rounds
                WHERE player_name = {playerName:String}
                ORDER BY round_end_time DESC
                LIMIT {roundCount:UInt32}";

            var results = new List<PlayerRound>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("roundCount", roundCount));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new PlayerRound
                {
                    PlayerName = reader.GetString(0),
                    RoundId = reader.GetString(1),
                    ServerGuid = reader.GetString(2),
                    MapName = reader.GetString(3),
                    FinalKills = Convert.ToUInt32(reader.GetValue(4)),
                    FinalDeaths = Convert.ToUInt32(reader.GetValue(5)),
                    FinalScore = Convert.ToInt32(reader.GetValue(6)),
                    PlayTimeMinutes = (int)Math.Round(Convert.ToDouble(reader.GetValue(7))),
                    RoundEndTime = reader.GetDateTime(8)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent rounds for player {PlayerName}", playerName);
            throw;
        }
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
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, MAX(value) as best_streak, COUNT(*) as streak_count
                FROM player_achievements
                WHERE achievement_type = 'kill_streak'
                GROUP BY player_name
                ORDER BY best_streak DESC, streak_count DESC
                LIMIT {limit:UInt32}";

            var entries = new List<LeaderboardEntry>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("limit", limit));
            
            await using var reader = await command.ExecuteReaderAsync();
            int rank = 1;
            while (await reader.ReadAsync())
            {
                var bestStreak = Convert.ToInt32(reader.GetValue(1));
                entries.Add(new LeaderboardEntry
                {
                    Rank = rank++,
                    PlayerName = reader.GetString(0),
                    Value = bestStreak,
                    DisplayValue = $"{bestStreak} kill streak",
                    AchievementCount = Convert.ToInt32(reader.GetValue(2))
                });
            }

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get kill streak leaderboard");
            throw;
        }
    }

    // Helper method to create parameters
    private System.Data.Common.DbParameter CreateParameter(string name, object value)
    {
        var param = _connection.CreateCommand().CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        return param;
    }

    // HTTP-based methods for bulk CSV operations only (not for user input queries)
    private async Task ExecuteQueryAsync(string query)
    {
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{clickHouseUrl.TrimEnd('/')}/", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"ClickHouse bulk insert failed: {response.StatusCode} - {errorContent}");
        }
    }

    // Performance calculation methods using player_metrics
    
    /// <summary>
    /// Get player's recent performance stats from player_metrics aggregated by round
    /// </summary>
    public async Task<List<RoundPerformance>> GetPlayerRecentPerformanceAsync(string playerName, int roundCount = 50)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            // Aggregate player_metrics by estimated rounds based on server/map/time gaps
            var query = @"
                WITH round_groups AS (
                    SELECT 
                        player_name,
                        server_guid,
                        map_name,
                        toStartOfHour(timestamp) as hour_group,
                        min(timestamp) as round_start,
                        max(timestamp) as round_end,
                        max(kills) - min(kills) as round_kills,
                        max(deaths) - min(deaths) as round_deaths,
                        max(score) - min(score) as round_score
                    FROM player_metrics
                    WHERE player_name = {playerName:String}
                    AND timestamp >= now() - INTERVAL 30 DAY
                    GROUP BY player_name, server_guid, map_name, hour_group
                    HAVING round_kills > 0 OR round_deaths > 0
                )
                SELECT 
                    round_start,
                    round_end,
                    round_kills,
                    round_deaths,
                    round_score,
                    dateDiff('minute', round_start, round_end) as play_time_minutes
                FROM round_groups
                WHERE play_time_minutes > 1 AND play_time_minutes < 120
                ORDER BY round_start DESC
                LIMIT {roundCount:UInt32}";

            var results = new List<RoundPerformance>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("roundCount", roundCount));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new RoundPerformance
                {
                    RoundStart = reader.GetDateTime(0),
                    RoundEnd = reader.GetDateTime(1),
                    Kills = Convert.ToUInt32(reader.GetValue(2)),
                    Deaths = Convert.ToUInt32(reader.GetValue(3)),
                    Score = Convert.ToInt32(reader.GetValue(4)),
                    PlayTimeMinutes = Convert.ToDouble(reader.GetValue(5))
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent performance for player {PlayerName}", playerName);
            throw;
        }
    }

    /// <summary>
    /// Get player metrics for a specific round timeframe
    /// </summary>
    public async Task<List<PlayerMetricPoint>> GetPlayerMetricsForRoundAsync(
        string playerName, 
        string serverGuid, 
        string mapName, 
        DateTime startTime, 
        DateTime endTime)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT timestamp, kills, deaths, score
                FROM player_metrics
                WHERE player_name = {playerName:String}
                AND server_guid = {serverGuid:String}
                AND map_name = {mapName:String}
                AND timestamp >= {startTime:DateTime}
                AND timestamp <= {endTime:DateTime}
                ORDER BY timestamp ASC";

            var results = new List<PlayerMetricPoint>();
            
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            
            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("serverGuid", serverGuid));
            command.Parameters.Add(CreateParameter("mapName", mapName));
            command.Parameters.Add(CreateParameter("startTime", startTime));
            command.Parameters.Add(CreateParameter("endTime", endTime));
            
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new PlayerMetricPoint
                {
                    Timestamp = reader.GetDateTime(0),
                    Kills = Convert.ToUInt16(reader.GetValue(1)),
                    Deaths = Convert.ToUInt16(reader.GetValue(2)),
                    Score = Convert.ToInt32(reader.GetValue(3))
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player metrics for round");
            throw;
        }
    }

    /// <summary>
    /// Calculate KPM performance statistics from player_metrics
    /// </summary>
    public async Task<PerformanceStats> CalculatePlayerKPMStatsAsync(string playerName, int minRounds = 25)
    {
        try
        {
            var recentPerformance = await GetPlayerRecentPerformanceAsync(playerName, 100);
            
            if (recentPerformance.Count < minRounds)
            {
                return new PerformanceStats { PlayerName = playerName };
            }

            var totalKills = recentPerformance.Sum(r => r.Kills);
            var totalMinutes = recentPerformance.Sum(r => r.PlayTimeMinutes);
            var kpm = totalMinutes > 0 ? totalKills / totalMinutes : 0;

            var totalDeaths = recentPerformance.Sum(r => r.Deaths);
            var kdRatio = totalDeaths > 0 ? (double)totalKills / totalDeaths : totalKills;

            return new PerformanceStats
            {
                PlayerName = playerName,
                KillsPerMinute = kpm,
                KillDeathRatio = kdRatio,
                RoundsAnalyzed = recentPerformance.Count,
                TotalKills = (int)totalKills,
                TotalDeaths = (int)totalDeaths,
                TotalMinutes = totalMinutes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate KPM stats for player {PlayerName}", playerName);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents performance data for a single round calculated from player_metrics
/// </summary>
public class RoundPerformance
{
    public DateTime RoundStart { get; set; }
    public DateTime RoundEnd { get; set; }
    public uint Kills { get; set; }
    public uint Deaths { get; set; }
    public int Score { get; set; }
    public double PlayTimeMinutes { get; set; }
}

/// <summary>
/// Represents a single player metric point in time
/// </summary>
public class PlayerMetricPoint
{
    public DateTime Timestamp { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public int Score { get; set; }
}

/// <summary>
/// Performance statistics calculated from player_metrics
/// </summary>
public class PerformanceStats
{
    public string PlayerName { get; set; } = "";
    public double KillsPerMinute { get; set; }
    public double KillDeathRatio { get; set; }
    public int RoundsAnalyzed { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double TotalMinutes { get; set; }
} 