using api.Gamification.Models;
using api.ClickHouse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ClickHouse.Client.ADO;

namespace api.Gamification.Services;

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
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

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

            // Use current timestamp for version to ensure each insert has a unique version
            var insertVersion = DateTime.UtcNow;

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
                Metadata = a.Metadata,
                Version = insertVersion.ToString("yyyy-MM-dd HH:mm:ss"),
                Game = a.Game ?? "unknown"
            }));

            var csvData = stringWriter.ToString();
            var query = "INSERT INTO player_achievements (player_name, achievement_type, achievement_id, achievement_name, tier, value, achieved_at, processed_at, server_guid, map_name, round_id, metadata, version, game) FORMAT CSV";
            var fullRequest = query + "\n" + csvData;

            await ExecuteQueryAsync(fullRequest);
            _logger.LogInformation("Successfully inserted {Count} achievements to ClickHouse with version {Version}", achievements.Count, insertVersion);
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

    /// <summary>
    /// Get the last processed timestamp specifically for placement achievements
    /// </summary>
    public async Task<DateTime> GetLastPlacementProcessedTimestampAsync()
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT MAX(processed_at) as last_processed 
                FROM player_achievements_deduplicated 
                WHERE achievement_type = {achievementType:String}";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("achievementType", AchievementTypes.Placement));

            var result = await command.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value && DateTime.TryParse(result.ToString(), out var lastProcessed))
            {
                return lastProcessed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last placement processed timestamp, returning minimum date");
        }

        return DateTime.MinValue;
    }

    /// <summary>
    /// Get the last processed timestamp specifically for team victory achievements
    /// Uses MAX of both team_victory and team_victory_switched, then subtracts 120 minutes as buffer
    /// This ensures we don't reprocess the same achievements while providing a safe buffer
    /// </summary>
    public async Task<DateTime> GetLastTeamVictoryProcessedTimestampAsync()
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            // Get the maximum processed timestamp from either achievement type
            // If one type has no records, it won't affect the other
            var query = @"
                SELECT MAX(processed_at) as last_processed
                FROM player_achievements_deduplicated 
                WHERE achievement_type = {achievementType:String} 
                AND achievement_id IN ('team_victory', 'team_victory_switched')";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add(CreateParameter("achievementType", AchievementTypes.TeamVictory));

            var result = await command.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value && DateTime.TryParse(result.ToString(), out var lastProcessed))
            {
                // Subtract 120 minutes as a round buffer to ensure we don't miss any achievements
                // This accounts for rounds that might have been processed but had late-arriving data
                return lastProcessed.AddMinutes(-120);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last team victory processed timestamp, returning minimum date");
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
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata, version, game
                FROM player_achievements_deduplicated
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
                    Metadata = reader.GetString(11),
                    Version = reader.GetDateTime(12),
                    Game = reader.GetString(13)
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

    public async Task<List<Achievement>> GetPlayerAchievementsByTypeAsync(string playerName, string achievementType, int? limit = null, int? offset = null)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata, version, game
                FROM player_achievements_deduplicated
                WHERE player_name = {playerName:String}
                AND achievement_type = {achievementType:String}
                ORDER BY achieved_at DESC";

            // Add pagination if specified
            if (limit.HasValue)
            {
                query += $" LIMIT {limit.Value}";
                if (offset.HasValue)
                {
                    query += $" OFFSET {offset.Value}";
                }
            }

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
                    Metadata = reader.GetString(11),
                    Version = reader.GetDateTime(12),
                    Game = reader.GetString(13)
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

    /// <summary>
    /// Gets only the achievement IDs for a player by type - memory efficient for milestone checking
    /// </summary>
    public async Task<HashSet<string>> GetPlayerAchievementIdsByTypeAsync(string playerName, string achievementType)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT DISTINCT achievement_id
                FROM player_achievements_deduplicated
                WHERE player_name = {playerName:String}
                AND achievement_type = {achievementType:String}";

            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var command = _connection.CreateCommand();
            command.CommandText = query;

            command.Parameters.Add(CreateParameter("playerName", playerName));
            command.Parameters.Add(CreateParameter("achievementType", achievementType));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player achievement IDs by type for {PlayerName}, type {AchievementType}", playerName, achievementType);
            throw;
        }
    }

    public async Task<List<Achievement>> GetRoundAchievementsAsync(string roundId)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var query = @"
                SELECT player_name, achievement_type, achievement_id, achievement_name, tier,
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata, version, game
                FROM player_achievements_deduplicated
                WHERE round_id = {roundId:String}
                ORDER BY achieved_at ASC";

            var results = new List<Achievement>();

            await using var command = _connection.CreateCommand();
            command.CommandText = query;

            command.Parameters.Add(CreateParameter("roundId", roundId));

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
                    Metadata = reader.GetString(11),
                    Version = reader.GetDateTime(12),
                    Game = reader.GetString(13)
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get achievements for round {RoundId}", roundId);
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
                FROM player_achievements_deduplicated
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
                FROM player_achievements_deduplicated
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
                FROM player_achievements_deduplicated
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
                       value, achieved_at, processed_at, server_guid, map_name, round_id, metadata, version, game
                FROM player_achievements_deduplicated
                {whereClause}
                ORDER BY {sortField} {orderDirection}
                LIMIT {{pageSize:UInt32}} OFFSET {{offset:UInt32}}";

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
                       play_time_minutes, round_start_time, round_end_time, game
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
                    RoundEndTime = reader.GetDateTime(9),
                    Game = reader.GetString(10)
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
                       play_time_minutes, round_end_time, game
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
                    RoundEndTime = reader.GetDateTime(8),
                    Game = reader.GetString(9)
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
                       play_time_minutes, round_end_time, game
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
                    RoundEndTime = reader.GetDateTime(8),
                    Game = reader.GetString(9)
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
                FROM player_achievements_deduplicated
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

    // Placement summary and leaderboards
    public async Task<PlayerPlacementSummary> GetPlayerPlacementSummaryAsync(string playerName, string? serverGuid = null, string? mapName = null)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var where = new List<string> {
                "achievement_type = {type:String}",
                "player_name = {playerName:String}"
            };
            var parameters = new List<System.Data.Common.DbParameter>
            {
                CreateParameter("type", AchievementTypes.Placement),
                CreateParameter("playerName", playerName)
            };

            if (!string.IsNullOrWhiteSpace(serverGuid))
            {
                where.Add("server_guid = {serverGuid:String}");
                parameters.Add(CreateParameter("serverGuid", serverGuid!));
            }
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                where.Add("map_name = {mapName:String}");
                parameters.Add(CreateParameter("mapName", mapName!));
            }

            var whereClause = string.Join(" AND ", where);

            var query = $@"
                SELECT
                    sum(if(tier = 'gold', 1, 0)) AS first_places,
                    sum(if(tier = 'silver', 1, 0)) AS second_places,
                    sum(if(tier = 'bronze', 1, 0)) AS third_places,
                    anyHeavy(JSONExtractString(metadata, 'team_label')) AS any_team_label
                FROM player_achievements_deduplicated
                WHERE {whereClause}";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            foreach (var p in parameters) command.Parameters.Add(p);

            var summary = new PlayerPlacementSummary { PlayerName = playerName, ServerGuid = serverGuid, MapName = mapName };

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                summary.FirstPlaces = Convert.ToInt32(reader.GetValue(0));
                summary.SecondPlaces = Convert.ToInt32(reader.GetValue(1));
                summary.ThirdPlaces = Convert.ToInt32(reader.GetValue(2));
                var teamLabelObj = reader.GetValue(3);
                summary.BestTeamLabel = teamLabelObj == null || teamLabelObj is DBNull ? null : Convert.ToString(teamLabelObj);
            }

            // Determine best team by counting occurrences by team_label
            var teamQuery = $@"
                SELECT JSONExtractString(metadata, 'team_label') AS team_label, count() AS c
                FROM player_achievements_deduplicated
                WHERE {whereClause}
                GROUP BY team_label
                ORDER BY c DESC
                LIMIT 1";
            await using var teamCmd = _connection.CreateCommand();
            teamCmd.CommandText = teamQuery;
            foreach (var p in parameters) teamCmd.Parameters.Add(p);
            await using var teamReader = await teamCmd.ExecuteReaderAsync();
            if (await teamReader.ReadAsync())
            {
                var tlObj = teamReader.GetValue(0);
                summary.BestTeamLabel = tlObj == null || tlObj is DBNull ? summary.BestTeamLabel : Convert.ToString(tlObj);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get placement summary for player {PlayerName}", playerName);
            throw;
        }
    }

    public async Task<List<PlacementLeaderboardEntry>> GetPlacementLeaderboardAsync(string? serverGuid = null, string? mapName = null, int limit = 100)
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            var where = new List<string> { "achievement_type = {type:String}" };
            var parameters = new List<System.Data.Common.DbParameter> { CreateParameter("type", AchievementTypes.Placement) };
            if (!string.IsNullOrWhiteSpace(serverGuid)) { where.Add("server_guid = {serverGuid:String}"); parameters.Add(CreateParameter("serverGuid", serverGuid!)); }
            if (!string.IsNullOrWhiteSpace(mapName)) { where.Add("map_name = {mapName:String}"); parameters.Add(CreateParameter("mapName", mapName!)); }
            var whereClause = string.Join(" AND ", where);

            var query = $@"
                SELECT
                    player_name,
                    sum(if(tier = 'gold', 1, 0)) AS first_places,
                    sum(if(tier = 'silver', 1, 0)) AS second_places,
                    sum(if(tier = 'bronze', 1, 0)) AS third_places,
                    (first_places * 3 + second_places * 2 + third_places) as points
                FROM player_achievements_deduplicated
                WHERE {whereClause}
                GROUP BY player_name
                ORDER BY points DESC, first_places DESC, second_places DESC, third_places DESC
                LIMIT {limit:UInt32}";

            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            foreach (var p in parameters) command.Parameters.Add(p);

            var entries = new List<PlacementLeaderboardEntry>();
            await using var reader = await command.ExecuteReaderAsync();
            int rank = 1;
            while (await reader.ReadAsync())
            {
                entries.Add(new PlacementLeaderboardEntry
                {
                    Rank = rank++,
                    PlayerName = reader.GetString(0),
                    FirstPlaces = Convert.ToInt32(reader.GetValue(1)),
                    SecondPlaces = Convert.ToInt32(reader.GetValue(2)),
                    ThirdPlaces = Convert.ToInt32(reader.GetValue(3))
                });
            }

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get placement leaderboard");
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
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

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