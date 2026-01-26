using System.Text.Json;
using api.AdminData.Models;
using api.Data.Entities;
using api.Gamification.Services;
using api.PlayerTracking;
using api.Services.BackgroundJobs;
using api.StatsCollectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.AdminData;

public class AdminDataService(
    PlayerTrackerDbContext dbContext,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AdminDataService> logger
) : IAdminDataService
{
    public async Task<PagedResult<SuspiciousSessionResponse>> QuerySuspiciousSessionsAsync(QuerySuspiciousSessionsRequest request)
    {
        var query = from ps in dbContext.PlayerSessions
                    join r in dbContext.Rounds on ps.RoundId equals r.RoundId
                    join s in dbContext.Servers on r.ServerGuid equals s.Guid
                    select new { ps, r, s };

        if (!request.IncludeDeletedRounds)
        {
            query = query.Where(x => !x.r.IsDeleted && !x.ps.IsDeleted);
        }

        // Filter by game type (from server record: bf1942, fh2, bfvietnam)
        if (!string.IsNullOrWhiteSpace(request.Game))
        {
            var game = request.Game.Trim().ToLowerInvariant();
            query = query.Where(x => x.s.Game == game);
        }

        // Apply filters (empty string / null from UI are treated as not provided)
        if (!string.IsNullOrEmpty(request.ServerGuid))
        {
            query = query.Where(x => x.ps.ServerGuid == request.ServerGuid);
        }

        if (request.MinScore.HasValue)
        {
            query = query.Where(x => x.ps.TotalScore >= request.MinScore.Value);
        }

        if (request.MinKdRatio.HasValue)
        {
            query = query.Where(x => x.ps.TotalDeaths > 0
                ? (double)x.ps.TotalKills / x.ps.TotalDeaths >= request.MinKdRatio.Value
                : x.ps.TotalKills >= request.MinKdRatio.Value);
        }

        if (request.StartDate.HasValue)
        {
            var startDateTime = request.StartDate.Value.ToDateTimeUtc();
            query = query.Where(x => x.ps.StartTime >= startDateTime);
        }

        if (request.EndDate.HasValue)
        {
            var endDateTime = request.EndDate.Value.ToDateTimeUtc();
            query = query.Where(x => x.ps.StartTime <= endDateTime);
        }

        // Get total count
        var totalItems = await query.CountAsync();

        // Apply pagination and projection
        var items = await query
            .OrderByDescending(x => x.ps.TotalScore)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new SuspiciousSessionResponse(
                x.ps.PlayerName,
                x.r.ServerName,
                x.ps.TotalScore,
                x.ps.TotalKills,
                x.ps.TotalDeaths,
                x.ps.TotalDeaths > 0 ? Math.Round((double)x.ps.TotalKills / x.ps.TotalDeaths, 2) : x.ps.TotalKills,
                x.ps.RoundId ?? "",
                x.r.StartTime,
                x.r.IsDeleted
            ))
            .ToListAsync();

        return new PagedResult<SuspiciousSessionResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / request.PageSize)
        };
    }

    public async Task<RoundDetailResponse?> GetRoundDetailAsync(string roundId)
    {
        var round = await dbContext.Rounds
            .FirstOrDefaultAsync(r => r.RoundId == roundId);

        if (round == null)
        {
            return null;
        }

        var players = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .Select(ps => new RoundPlayerInfo(
                ps.PlayerName,
                ps.TotalScore,
                ps.TotalKills,
                ps.TotalDeaths
            ))
            .ToListAsync();

        var achievementCount = await dbContext.PlayerAchievements
            .CountAsync(pa => pa.RoundId == roundId);

        return new RoundDetailResponse(
            round.RoundId,
            round.ServerName,
            Instant.FromDateTimeUtc(DateTime.SpecifyKind(round.StartTime, DateTimeKind.Utc)),
            round.EndTime.HasValue
                ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(round.EndTime.Value, DateTimeKind.Utc))
                : null,
            round.MapName,
            players,
            achievementCount,
            round.IsDeleted
        );
    }

    public async Task<DeleteRoundResponse> DeleteRoundAsync(string roundId, string adminEmail)
    {
        var round = await dbContext.Rounds
            .FirstOrDefaultAsync(r => r.RoundId == roundId);

        if (round == null)
        {
            throw new InvalidOperationException($"Round {roundId} not found");
        }

        logger.LogInformation("Starting soft-delete for round {RoundId} (delete achievements, mark round+sessions deleted) requested by {AdminEmail}", roundId, adminEmail);

        // Step 1: Collect affected player names for aggregate recalculation (count for audit/response from .Count)
        var affectedPlayerNames = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .Select(ps => ps.PlayerName)
            .Distinct()
            .ToListAsync();

        logger.LogInformation("Round {RoundId} affects {PlayerCount} players", roundId, affectedPlayerNames.Count);

        // Step 2: Delete PlayerAchievements where RoundId = roundId (achievements can be rebuilt)
        var deletedAchievements = await dbContext.PlayerAchievements
            .Where(pa => pa.RoundId == roundId)
            .ExecuteDeleteAsync();
        logger.LogInformation("Deleted {Count} achievements for round {RoundId}", deletedAchievements, roundId);

        // Step 3: Mark all PlayerSessions for this round as IsDeleted (soft-delete; observations kept)
        var sessionsMarked = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.IsDeleted, true));
        logger.LogInformation("Marked {Count} sessions as deleted for round {RoundId}", sessionsMarked, roundId);

        // Step 4: Mark the Round as IsDeleted (soft-delete; round and sessions remain for potential recovery)
        await dbContext.Rounds
            .Where(r => r.RoundId == roundId)
            .ExecuteUpdateAsync(r => r.SetProperty(ro => ro.IsDeleted, true));
        logger.LogInformation("Marked round {RoundId} as deleted", roundId);

        // Step 5: Create audit log entry (player count only; players are soft-deleted and recoverable)
        var details = JsonSerializer.Serialize(new
        {
            DeletedAchievements = deletedAchievements,
            SessionsMarkedDeleted = sessionsMarked,
            RoundMarkedDeleted = 1,
            AffectedPlayers = affectedPlayerNames.Count,
            ServerGuid = round.ServerGuid,
            MapName = round.MapName,
            RoundStartTime = round.StartTime
        });

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            Action = "delete_round",
            TargetType = "Round",
            TargetId = roundId,
            Details = details,
            AdminEmail = adminEmail,
            Timestamp = clock.GetCurrentInstant()
        });
        await dbContext.SaveChangesAsync();

        // Step 6: Queue aggregate recalculation (player aggregates, ServerMapStats, ServerPlayerRankings)
        var serverGuid = round.ServerGuid;
        var mapName = round.MapName ?? "";
        var year = round.StartTime.Year;
        var month = round.StartTime.Month;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var aggregateBackfill = scope.ServiceProvider.GetRequiredService<IAggregateBackfillBackgroundService>();
                var dailyRefresh = scope.ServiceProvider.GetRequiredService<IDailyAggregateRefreshBackgroundService>();
                var rankingsRecalc = scope.ServiceProvider.GetRequiredService<IServerPlayerRankingsRecalculationService>();

                if (affectedPlayerNames.Count > 0)
                {
                    await aggregateBackfill.RunForPlayersAsync(affectedPlayerNames);
                    logger.LogInformation("Aggregate recalculation completed for {PlayerCount} players after soft-deleting round {RoundId}",
                        affectedPlayerNames.Count, roundId);
                }
                if (!string.IsNullOrEmpty(mapName))
                {
                    await dailyRefresh.RefreshServerMapStatsForServerMapPeriodAsync(serverGuid, mapName, year, month);
                }
                await rankingsRecalc.RecalculateForServerAndPeriodAsync(serverGuid, year, month);

                if (affectedPlayerNames.Count > 0)
                {
                    var milestoneCalculator = scope.ServiceProvider.GetRequiredService<MilestoneCalculator>();
                    await milestoneCalculator.RemoveInvalidMilestoneAchievementsForPlayersAsync(affectedPlayerNames);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recalculate aggregates after soft-deleting round {RoundId}", roundId);
            }
        });

        // DeletedObservations=0 (we keep observations); DeletedSessions/DeletedRounds = marked count for API shape
        return new DeleteRoundResponse(
            roundId,
            deletedAchievements,
            0,
            sessionsMarked,
            1,
            affectedPlayerNames.Count
        );
    }

    public async Task<BulkDeleteRoundsResponse> BulkDeleteRoundsAsync(IReadOnlyList<string> roundIds, string adminEmail)
    {
        if (roundIds == null || roundIds.Count == 0)
        {
            throw new ArgumentException("At least one round ID is required", nameof(roundIds));
        }

        var distinctIds = roundIds.Distinct().ToList();
        var rounds = await dbContext.Rounds
            .Where(r => distinctIds.Contains(r.RoundId))
            .ToListAsync();

        var foundIds = new HashSet<string>(rounds.Select(r => r.RoundId));
        var missing = distinctIds.FirstOrDefault(id => !foundIds.Contains(id));
        if (missing != null)
        {
            throw new InvalidOperationException($"Round {missing} not found");
        }

        logger.LogInformation(
            "Starting bulk soft-delete for {Count} rounds (delete achievements, mark rounds+sessions deleted) requested by {AdminEmail}",
            distinctIds.Count, adminEmail);

        // Collect affected player names (union across all rounds)
        var affectedPlayerNames = await dbContext.PlayerSessions
            .Where(ps => distinctIds.Contains(ps.RoundId ?? ""))
            .Select(ps => ps.PlayerName)
            .Distinct()
            .ToListAsync();

        // Bulk delete achievements for all rounds
        var deletedAchievements = await dbContext.PlayerAchievements
            .Where(pa => distinctIds.Contains(pa.RoundId ?? ""))
            .ExecuteDeleteAsync();
        logger.LogInformation("Deleted {Count} achievements for {RoundCount} rounds", deletedAchievements, distinctIds.Count);

        // Bulk mark sessions as deleted
        var sessionsMarked = await dbContext.PlayerSessions
            .Where(ps => distinctIds.Contains(ps.RoundId ?? ""))
            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.IsDeleted, true));
        logger.LogInformation("Marked {Count} sessions as deleted for {RoundCount} rounds", sessionsMarked, distinctIds.Count);

        // Bulk mark rounds as deleted
        await dbContext.Rounds
            .Where(r => distinctIds.Contains(r.RoundId))
            .ExecuteUpdateAsync(r => r.SetProperty(ro => ro.IsDeleted, true));
        logger.LogInformation("Marked {Count} rounds as deleted", distinctIds.Count);

        var details = JsonSerializer.Serialize(new
        {
            RoundIds = distinctIds,
            DeletedAchievements = deletedAchievements,
            SessionsMarkedDeleted = sessionsMarked,
            RoundsMarkedDeleted = distinctIds.Count,
            AffectedPlayers = affectedPlayerNames.Count,
        });

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            Action = "bulk_delete_rounds",
            TargetType = "Round",
            TargetId = "bulk",
            Details = details,
            AdminEmail = adminEmail,
            Timestamp = clock.GetCurrentInstant()
        });
        await dbContext.SaveChangesAsync();

        // Distinct (ServerGuid, MapName, Year, Month) for RefreshServerMapStats
        var serverMapPeriods = rounds
            .Where(r => !string.IsNullOrEmpty(r.MapName))
            .Select(r => (r.ServerGuid, MapName: r.MapName ?? "", r.StartTime.Year, r.StartTime.Month))
            .Distinct()
            .ToList();
        // Distinct (ServerGuid, Year, Month) for RecalculateForServerAndPeriod
        var serverPeriods = rounds
            .Select(r => (r.ServerGuid, r.StartTime.Year, r.StartTime.Month))
            .Distinct()
            .ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var aggregateBackfill = scope.ServiceProvider.GetRequiredService<IAggregateBackfillBackgroundService>();
                var dailyRefresh = scope.ServiceProvider.GetRequiredService<IDailyAggregateRefreshBackgroundService>();
                var rankingsRecalc = scope.ServiceProvider.GetRequiredService<IServerPlayerRankingsRecalculationService>();

                if (affectedPlayerNames.Count > 0)
                {
                    await aggregateBackfill.RunForPlayersAsync(affectedPlayerNames);
                    logger.LogInformation(
                        "Aggregate recalculation completed for {PlayerCount} players after bulk soft-deleting {RoundCount} rounds",
                        affectedPlayerNames.Count, distinctIds.Count);
                }
                foreach (var (serverGuid, mapName, year, month) in serverMapPeriods)
                {
                    await dailyRefresh.RefreshServerMapStatsForServerMapPeriodAsync(serverGuid, mapName, year, month);
                }
                foreach (var (serverGuid, year, month) in serverPeriods)
                {
                    await rankingsRecalc.RecalculateForServerAndPeriodAsync(serverGuid, year, month);
                }
                if (affectedPlayerNames.Count > 0)
                {
                    var milestoneCalculator = scope.ServiceProvider.GetRequiredService<MilestoneCalculator>();
                    await milestoneCalculator.RemoveInvalidMilestoneAchievementsForPlayersAsync(affectedPlayerNames);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recalculate aggregates after bulk soft-deleting {RoundCount} rounds", distinctIds.Count);
            }
        });

        return new BulkDeleteRoundsResponse(distinctIds.Count, deletedAchievements, sessionsMarked, affectedPlayerNames.Count);
    }

    public async Task<UndeleteRoundResponse> UndeleteRoundAsync(string roundId, string adminEmail)
    {
        var round = await dbContext.Rounds
            .FirstOrDefaultAsync(r => r.RoundId == roundId);

        if (round == null)
        {
            throw new InvalidOperationException($"Round {roundId} not found");
        }

        logger.LogInformation("Undelete (restore) round {RoundId} requested by {AdminEmail}", roundId, adminEmail);

        var affectedPlayerNames = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .Select(ps => ps.PlayerName)
            .Distinct()
            .ToListAsync();

        var sessionsRestored = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.IsDeleted, false));

        await dbContext.Rounds
            .Where(r => r.RoundId == roundId)
            .ExecuteUpdateAsync(r => r.SetProperty(ro => ro.IsDeleted, false));

        var details = JsonSerializer.Serialize(new
        {
            SessionsRestored = sessionsRestored,
            RoundRestored = 1,
            AffectedPlayers = affectedPlayerNames.Count,
            ServerGuid = round.ServerGuid,
            MapName = round.MapName,
            RoundStartTime = round.StartTime
        });

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            Action = "undelete_round",
            TargetType = "Round",
            TargetId = roundId,
            Details = details,
            AdminEmail = adminEmail,
            Timestamp = clock.GetCurrentInstant()
        });
        await dbContext.SaveChangesAsync();

        var serverGuid = round.ServerGuid;
        var mapName = round.MapName ?? "";
        var year = round.StartTime.Year;
        var month = round.StartTime.Month;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var aggregateBackfill = scope.ServiceProvider.GetRequiredService<IAggregateBackfillBackgroundService>();
                var dailyRefresh = scope.ServiceProvider.GetRequiredService<IDailyAggregateRefreshBackgroundService>();
                var rankingsRecalc = scope.ServiceProvider.GetRequiredService<IServerPlayerRankingsRecalculationService>();

                if (affectedPlayerNames.Count > 0)
                {
                    await aggregateBackfill.RunForPlayersAsync(affectedPlayerNames);
                    logger.LogInformation("Aggregate recalculation completed for {PlayerCount} players after restoring round {RoundId}", affectedPlayerNames.Count, roundId);
                }
                if (!string.IsNullOrEmpty(mapName))
                {
                    await dailyRefresh.RefreshServerMapStatsForServerMapPeriodAsync(serverGuid, mapName, year, month);
                }
                await rankingsRecalc.RecalculateForServerAndPeriodAsync(serverGuid, year, month);

                var gamificationService = scope.ServiceProvider.GetRequiredService<GamificationService>();
                await gamificationService.ProcessAchievementsForRoundIdAsync(roundId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recalculate aggregates after restoring round {RoundId}", roundId);
            }
        });

        return new UndeleteRoundResponse(roundId, sessionsRestored, 1, affectedPlayerNames.Count);
    }

    public async Task<List<AdminAuditLog>> GetAuditLogAsync(int limit = 100)
    {
        return await dbContext.AdminAuditLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

}
