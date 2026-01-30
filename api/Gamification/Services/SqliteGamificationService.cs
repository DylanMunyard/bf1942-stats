using api.Data.Entities;
using api.Gamification.Models;
using api.PlayerTracking;
using api.Analytics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.Diagnostics;

namespace api.Gamification.Services;

/// <summary>
/// SQLite-based gamification service for achievement storage and retrieval.
/// SQLite-backed gamification service.
/// </summary>
public class SqliteGamificationService(
    PlayerTrackerDbContext dbContext,
    ILogger<SqliteGamificationService> logger) : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Insert achievements in batch to SQLite
    /// </summary>
    public async Task InsertAchievementsBatchAsync(List<Achievement> achievements)
    {
        if (!achievements.Any()) return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert Achievement models to PlayerAchievement entities
            var playerAchievements = achievements.Select(MapAchievementToEntity).ToList();

            // Set version timestamp for deduplication
            var version = SystemClock.Instance.GetCurrentInstant();
            foreach (var pa in playerAchievements)
            {
                pa.Version = version;
            }

            // Use raw connection for efficient batch inserts with transaction
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var sql = @"
                    INSERT OR IGNORE INTO PlayerAchievements (
                        PlayerName, AchievementType, AchievementId, AchievementName, Tier, Value,
                        AchievedAt, ProcessedAt, ServerGuid, MapName, RoundId, Metadata, Game, Version
                    ) VALUES (
                        $PlayerName, $AchievementType, $AchievementId, $AchievementName, $Tier, $Value,
                        $AchievedAt, $ProcessedAt, $ServerGuid, $MapName, $RoundId, $Metadata, $Game, $Version
                    )";

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;

                // Create parameters once, reuse for each insert
                var pPlayerName = command.CreateParameter(); pPlayerName.ParameterName = "$PlayerName"; command.Parameters.Add(pPlayerName);
                var pAchievementType = command.CreateParameter(); pAchievementType.ParameterName = "$AchievementType"; command.Parameters.Add(pAchievementType);
                var pAchievementId = command.CreateParameter(); pAchievementId.ParameterName = "$AchievementId"; command.Parameters.Add(pAchievementId);
                var pAchievementName = command.CreateParameter(); pAchievementName.ParameterName = "$AchievementName"; command.Parameters.Add(pAchievementName);
                var pTier = command.CreateParameter(); pTier.ParameterName = "$Tier"; command.Parameters.Add(pTier);
                var pValue = command.CreateParameter(); pValue.ParameterName = "$Value"; command.Parameters.Add(pValue);
                var pAchievedAt = command.CreateParameter(); pAchievedAt.ParameterName = "$AchievedAt"; command.Parameters.Add(pAchievedAt);
                var pProcessedAt = command.CreateParameter(); pProcessedAt.ParameterName = "$ProcessedAt"; command.Parameters.Add(pProcessedAt);
                var pServerGuid = command.CreateParameter(); pServerGuid.ParameterName = "$ServerGuid"; command.Parameters.Add(pServerGuid);
                var pMapName = command.CreateParameter(); pMapName.ParameterName = "$MapName"; command.Parameters.Add(pMapName);
                var pRoundId = command.CreateParameter(); pRoundId.ParameterName = "$RoundId"; command.Parameters.Add(pRoundId);
                var pMetadata = command.CreateParameter(); pMetadata.ParameterName = "$Metadata"; command.Parameters.Add(pMetadata);
                var pGame = command.CreateParameter(); pGame.ParameterName = "$Game"; command.Parameters.Add(pGame);
                var pVersion = command.CreateParameter(); pVersion.ParameterName = "$Version"; command.Parameters.Add(pVersion);

                // Prepare statement for reuse
                await command.PrepareAsync();

                foreach (var achievement in playerAchievements)
                {
                    pPlayerName.Value = achievement.PlayerName;
                    pAchievementType.Value = achievement.AchievementType;
                    pAchievementId.Value = achievement.AchievementId;
                    pAchievementName.Value = achievement.AchievementName;
                    pTier.Value = achievement.Tier;
                    pValue.Value = achievement.Value;
                    pAchievedAt.Value = achievement.AchievedAt.ToString();
                    pProcessedAt.Value = achievement.ProcessedAt.ToString();
                    pServerGuid.Value = achievement.ServerGuid;
                    pMapName.Value = achievement.MapName;
                    pRoundId.Value = achievement.RoundId;
                    pMetadata.Value = achievement.Metadata ?? (object)DBNull.Value;
                    pGame.Value = achievement.Game;
                    pVersion.Value = achievement.Version.ToString();

                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            stopwatch.Stop();
            logger.LogInformation(
                "Processed {Count} achievements for SQLite insertion in {ElapsedMs}ms (duplicates ignored)",
                achievements.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex,
                "Failed to process {Count} achievements for SQLite insertion after {ElapsedMs}ms",
                achievements.Count, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Get the last processed timestamp for regular achievements
    /// </summary>
    public async Task<DateTime> GetLastProcessedTimestampAsync()
    {
        try
        {
            var lastProcessedInstant = await dbContext.PlayerAchievements
                .Where(pa => pa.AchievementType != AchievementTypes.Placement &&
                            pa.AchievementType != AchievementTypes.TeamVictory &&
                            pa.AchievementType != AchievementTypes.TeamVictorySwitched)
                .MaxAsync(pa => (Instant?)pa.ProcessedAt);

            var lastProcessed = lastProcessedInstant?.ToDateTimeUtc() ?? DateTime.MinValue;
            logger.LogDebug("Last processed timestamp for achievements: {Timestamp}", lastProcessed);
            return lastProcessed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get last processed timestamp, returning minimum date");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Get the last processed timestamp for placement achievements
    /// </summary>
    public async Task<DateTime> GetLastPlacementProcessedTimestampAsync()
    {
        try
        {
            var lastProcessedInstant = await dbContext.PlayerAchievements
                .Where(pa => pa.AchievementType == AchievementTypes.Placement)
                .MaxAsync(pa => (Instant?)pa.ProcessedAt);

            var lastProcessed = lastProcessedInstant?.ToDateTimeUtc() ?? DateTime.MinValue;
            logger.LogDebug("Last processed timestamp for placements: {Timestamp}", lastProcessed);
            return lastProcessed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get last placement processed timestamp, returning minimum date");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Get the last processed timestamp for team victory achievements
    /// </summary>
    public async Task<DateTime> GetLastTeamVictoryProcessedTimestampAsync()
    {
        try
        {
            var lastProcessedInstant = await dbContext.PlayerAchievements
                .Where(pa => pa.AchievementType == AchievementTypes.TeamVictory ||
                            pa.AchievementType == AchievementTypes.TeamVictorySwitched)
                .MaxAsync(pa => (Instant?)pa.ProcessedAt);

            var lastProcessed = lastProcessedInstant?.ToDateTimeUtc() ?? DateTime.MinValue;
            logger.LogDebug("Last processed timestamp for team victories: {Timestamp}", lastProcessed);
            return lastProcessed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get last team victory processed timestamp, returning minimum date");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Get player's achievements with limit
    /// </summary>
    public async Task<List<Achievement>> GetPlayerAchievementsAsync(string playerName, int limit = 50)
    {
        try
        {
            var playerAchievements = await dbContext.PlayerAchievements
                .Where(pa => pa.PlayerName == playerName)
                .OrderByDescending(pa => pa.AchievedAt)
                .Take(limit)
                .ToListAsync();

            return playerAchievements.Select(MapEntityToAchievement).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievements for player {PlayerName}", playerName);
            return new List<Achievement>();
        }
    }

    /// <summary>
    /// Get grouped achievement counts for a player
    /// </summary>
    public async Task<List<PlayerAchievementGroup>> GetPlayerAchievementGroupsAsync(string playerName)
    {
        try
        {
            var grouped = await dbContext.PlayerAchievements
                .Where(pa => pa.PlayerName == playerName)
                .GroupBy(pa => new
                {
                    pa.AchievementId,
                    pa.AchievementName,
                    pa.AchievementType,
                    pa.Tier,
                    pa.Game
                })
                .Select(g => new
                {
                    g.Key.AchievementId,
                    g.Key.AchievementName,
                    g.Key.AchievementType,
                    g.Key.Tier,
                    g.Key.Game,
                    Count = g.Count(),
                    LatestValue = g.Max(pa => pa.Value),
                    LatestAchievedAt = g.Max(pa => pa.AchievedAt)
                })
                .ToListAsync();

            return grouped.Select(g => new PlayerAchievementGroup
            {
                AchievementId = g.AchievementId,
                AchievementName = g.AchievementName,
                AchievementType = g.AchievementType,
                Tier = g.Tier,
                Game = g.Game,
                Count = g.Count,
                LatestValue = g.LatestValue,
                LatestAchievedAt = g.LatestAchievedAt.ToDateTimeUtc()
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get grouped achievements for player {PlayerName}", playerName);
            return new List<PlayerAchievementGroup>();
        }
    }

    /// <summary>
    /// Get hero achievements for a player (5 most recent achievements with full details)
    /// </summary>
    public async Task<List<Achievement>> GetPlayerHeroAchievementsAsync(string playerName)
    {
        try
        {
            // Get the 5 most recent achievements
            var recentAchievementsQuery = @"
                SELECT pa.PlayerName, pa.AchievementType, pa.AchievementId, pa.AchievementName,
                       pa.Tier, pa.Value, pa.AchievedAt, pa.ProcessedAt, pa.ServerGuid,
                       pa.MapName, pa.RoundId, pa.Metadata, pa.Game, pa.Version
                FROM PlayerAchievements pa
                WHERE pa.PlayerName = @playerName
                ORDER BY pa.AchievedAt DESC
                LIMIT 5";

            var achievements = await dbContext.PlayerAchievements
                .FromSqlRaw(recentAchievementsQuery,
                    new Microsoft.Data.Sqlite.SqliteParameter("@playerName", playerName))
                .Select(pa => new Achievement
                {
                    PlayerName = pa.PlayerName,
                    AchievementType = pa.AchievementType,
                    AchievementId = pa.AchievementId,
                    AchievementName = pa.AchievementName,
                    Tier = pa.Tier,
                    Value = (uint)pa.Value,
                    AchievedAt = pa.AchievedAt.ToDateTimeUtc(),
                    ProcessedAt = pa.ProcessedAt.ToDateTimeUtc(),
                    ServerGuid = pa.ServerGuid,
                    MapName = pa.MapName,
                    RoundId = pa.RoundId,
                    Metadata = pa.Metadata ?? "",
                    Game = pa.Game,
                    Version = pa.AchievedAt.ToDateTimeUtc()
                })
                .ToListAsync();

            return achievements;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get hero achievements for player {PlayerName}", playerName);
            return new List<Achievement>();
        }
    }

    /// <summary>
    /// Get player's achievements by type
    /// </summary>
    public async Task<List<Achievement>> GetPlayerAchievementsByTypeAsync(
        string playerName,
        string achievementType,
        int? limit = null,
        int? offset = null)
    {
        try
        {
            IQueryable<PlayerAchievement> query = dbContext.PlayerAchievements
                .Where(pa => pa.PlayerName == playerName && pa.AchievementType == achievementType)
                .OrderByDescending(pa => pa.AchievedAt);

            if (offset.HasValue)
            {
                query = query.Skip(offset.Value);
            }

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            var playerAchievements = await query.ToListAsync();
            return playerAchievements.Select(MapEntityToAchievement).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to get {Type} achievements for player {PlayerName}",
                achievementType, playerName);
            return new List<Achievement>();
        }
    }

    /// <summary>
    /// Get player's achievement IDs by type
    /// </summary>
    public async Task<HashSet<string>> GetPlayerAchievementIdsByTypeAsync(string playerName, string achievementType)
    {
        try
        {
            var achievementIds = await dbContext.PlayerAchievements
                .Where(pa => pa.PlayerName == playerName && pa.AchievementType == achievementType)
                .Select(pa => pa.AchievementId)
                .Distinct()
                .ToListAsync();

            return achievementIds.ToHashSet();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to get {Type} achievement IDs for player {PlayerName}",
                achievementType, playerName);
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// Get achievements for a specific round
    /// </summary>
    public async Task<List<Achievement>> GetRoundAchievementsAsync(string roundId)
    {
        try
        {
            var playerAchievements = await dbContext.PlayerAchievements
                .Where(pa => pa.RoundId == roundId)
                .OrderBy(pa => pa.AchievedAt)
                .ToListAsync();

            return playerAchievements.Select(MapEntityToAchievement).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievements for round {RoundId}", roundId);
            return new List<Achievement>();
        }
    }

    /// <summary>
    /// Get all achievement IDs for a player
    /// </summary>
    public async Task<List<string>> GetPlayerAchievementIdsAsync(string playerName)
    {
        try
        {
            return await dbContext.PlayerAchievements
                .Where(pa => pa.PlayerName == playerName)
                .Select(pa => pa.AchievementId)
                .Distinct()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievement IDs for player {PlayerName}", playerName);
            return new List<string>();
        }
    }

    /// <summary>
    /// Get achievements with pagination and filtering
    /// </summary>
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
            var query = dbContext.PlayerAchievements.AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                query = query.Where(pa => pa.PlayerName.Contains(playerName));
            }

            if (!string.IsNullOrWhiteSpace(achievementType))
            {
                query = query.Where(pa => pa.AchievementType == achievementType);
            }

            if (!string.IsNullOrWhiteSpace(achievementId))
            {
                query = query.Where(pa => pa.AchievementId.Contains(achievementId));
            }

            if (!string.IsNullOrWhiteSpace(tier))
            {
                query = query.Where(pa => pa.Tier == tier);
            }

            if (achievedFrom.HasValue)
            {
                var fromInstant = Instant.FromDateTimeUtc(achievedFrom.Value);
                query = query.Where(pa => pa.AchievedAt >= fromInstant);
            }

            if (achievedTo.HasValue)
            {
                var toInstant = Instant.FromDateTimeUtc(achievedTo.Value);
                query = query.Where(pa => pa.AchievedAt <= toInstant);
            }

            if (!string.IsNullOrWhiteSpace(serverGuid))
            {
                query = query.Where(pa => pa.ServerGuid == serverGuid);
            }

            if (!string.IsNullOrWhiteSpace(mapName))
            {
                query = query.Where(pa => pa.MapName.Contains(mapName));
            }

            // Get total count
            var totalCount = await query.CountAsync();

            // Apply sorting
            query = sortBy.ToLower() switch
            {
                "playername" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.PlayerName)
                    : query.OrderByDescending(pa => pa.PlayerName),
                "achievementtype" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.AchievementType)
                    : query.OrderByDescending(pa => pa.AchievementType),
                "achievementid" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.AchievementId)
                    : query.OrderByDescending(pa => pa.AchievementId),
                "tier" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.Tier)
                    : query.OrderByDescending(pa => pa.Tier),
                "value" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.Value)
                    : query.OrderByDescending(pa => pa.Value),
                "achievedat" => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.AchievedAt)
                    : query.OrderByDescending(pa => pa.AchievedAt),
                _ => sortOrder.ToLower() == "asc"
                    ? query.OrderBy(pa => pa.AchievedAt)
                    : query.OrderByDescending(pa => pa.AchievedAt)
            };

            // Apply pagination
            var achievements = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (achievements.Select(MapEntityToAchievement).ToList(), totalCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get achievements with paging");
            return (new List<Achievement>(), 0);
        }
    }

    public async Task<List<PlayerRound>> GetPlayerRoundsSinceAsync(DateTime sinceTime)
    {
        try
        {
            // Query PlayerSessions that have completed (have RoundId and are closed)
            var sessions = await dbContext.PlayerSessions
                .Include(ps => ps.Player)
                .Where(ps => ps.StartTime >= sinceTime &&
                            ps.RoundId != null &&
                            !ps.IsActive &&
                            ps.TotalKills >= 0) // Basic validation
                .OrderBy(ps => ps.StartTime)
                .ToListAsync();

            // Convert PlayerSessions to PlayerRound format expected by achievement calculators
            var rounds = sessions.Select(MapPlayerSessionToPlayerRound).ToList();

            logger.LogInformation("Found {SessionCount} player sessions since {SinceTime}, converted to {RoundCount} rounds",
                sessions.Count, sinceTime, rounds.Count);

            return rounds;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get player rounds since {SinceTime}", sinceTime);
            return [];
        }
    }

    /// <summary>
    /// Get PlayerRounds for a specific round. Used when reprocessing achievements after round undelete.
    /// Excludes soft-deleted sessions.
    /// </summary>
    public async Task<List<PlayerRound>> GetPlayerRoundsForRoundAsync(string roundId)
    {
        try
        {
            var sessions = await dbContext.PlayerSessions
                .Include(ps => ps.Player)
                .Where(ps => ps.RoundId == roundId && !ps.IsDeleted)
                .OrderBy(ps => ps.PlayerName)
                .ToListAsync();

            return sessions.Select(MapPlayerSessionToPlayerRound).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get player rounds for round {RoundId}", roundId);
            return [];
        }
    }

    public async Task<List<LeaderboardEntry>> GetKillStreakLeaderboardAsync(int limit = 100)
    {
        try
        {
            // Query kill streak achievements and aggregate by player
            var leaderboard = await dbContext.PlayerAchievements
                .Where(pa => pa.AchievementType == AchievementTypes.KillStreak)
                .GroupBy(pa => pa.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    MaxValue = g.Max(pa => pa.Value),
                    AchievementCount = g.Count()
                })
                .OrderByDescending(x => x.MaxValue)
                .ThenByDescending(x => x.AchievementCount)
                .Take(limit)
                .ToListAsync();

            var entries = leaderboard.Select((entry, index) => new LeaderboardEntry
            {
                Rank = index + 1,
                PlayerName = entry.PlayerName,
                Value = entry.MaxValue,
                DisplayValue = $"{entry.MaxValue} kills",
                AchievementCount = entry.AchievementCount,
                TopBadges = []
            }).ToList();

            logger.LogDebug("Generated kill streak leaderboard with {EntryCount} entries", entries.Count);

            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get kill streak leaderboard");
            return [];
        }
    }

    /// <summary>
    /// Map Achievement model to PlayerAchievement entity
    /// </summary>
    private static PlayerAchievement MapAchievementToEntity(Achievement achievement)
    {
        return new PlayerAchievement
        {
            PlayerName = achievement.PlayerName,
            AchievementType = achievement.AchievementType,
            AchievementId = achievement.AchievementId,
            AchievementName = achievement.AchievementName,
            Tier = achievement.Tier,
            Value = (int)achievement.Value,
            AchievedAt = Instant.FromDateTimeUtc(DateTime.SpecifyKind(achievement.AchievedAt, DateTimeKind.Utc)),
            ProcessedAt = Instant.FromDateTimeUtc(DateTime.SpecifyKind(achievement.ProcessedAt, DateTimeKind.Utc)),
            ServerGuid = achievement.ServerGuid,
            MapName = achievement.MapName,
            RoundId = achievement.RoundId,
            Metadata = achievement.Metadata,
            Game = achievement.Game ?? "unknown"
        };
    }

    /// <summary>
    /// Map PlayerAchievement entity to Achievement model
    /// </summary>
    private static Achievement MapEntityToAchievement(PlayerAchievement entity)
    {
        return new Achievement
        {
            PlayerName = entity.PlayerName,
            AchievementType = entity.AchievementType,
            AchievementId = entity.AchievementId,
            AchievementName = entity.AchievementName,
            Tier = entity.Tier,
            Value = (uint)entity.Value,
            AchievedAt = entity.AchievedAt.ToDateTimeUtc(),
            ProcessedAt = entity.ProcessedAt.ToDateTimeUtc(),
            ServerGuid = entity.ServerGuid,
            MapName = entity.MapName,
            RoundId = entity.RoundId,
            Metadata = entity.Metadata ?? "",
            Game = entity.Game,
            Version = entity.Version.ToDateTimeUtc()
        };
    }

    /// <summary>
    /// Map PlayerSession entity to PlayerRound model for achievement processing
    /// </summary>
    private static PlayerRound MapPlayerSessionToPlayerRound(PlayerSession session)
    {
        return new PlayerRound
        {
            PlayerName = session.PlayerName,
            RoundId = session.RoundId!,
            RoundStartTime = session.StartTime,
            RoundEndTime = session.LastSeenTime,
            FinalScore = session.TotalScore,
            FinalKills = (uint)session.TotalKills,
            FinalDeaths = (uint)session.TotalDeaths,
            PlayTimeMinutes = (session.LastSeenTime - session.StartTime).TotalMinutes,
            ServerGuid = session.ServerGuid,
            MapName = session.MapName,
            TeamLabel = session.CurrentTeamLabel ?? "",
            GameId = session.GameType ?? "",
            Game = session.GameType ?? "",
            IsBot = session.Player?.AiBot ?? false,
            AveragePing = session.AveragePing
        };
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
