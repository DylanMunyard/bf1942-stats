using System.Text.Json;
using api.AdminData.Models;
using api.Data.Entities;
using api.PlayerTracking;
using api.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.AdminData;

public class AdminDataService(
    PlayerTrackerDbContext dbContext,
    IAggregateBackfillBackgroundService aggregateBackfillService,
    IClock clock,
    ILogger<AdminDataService> logger
) : IAdminDataService
{
    public async Task<PagedResult<SuspiciousSessionResponse>> QuerySuspiciousSessionsAsync(QuerySuspiciousSessionsRequest request)
    {
        var query = from ps in dbContext.PlayerSessions
                    join r in dbContext.Rounds on ps.RoundId equals r.RoundId
                    select new { ps, r };

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
                x.r.StartTime
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
            achievementCount
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

        logger.LogInformation("Starting cascade delete for round {RoundId} requested by {AdminEmail}", roundId, adminEmail);

        // Step 1: Collect affected player names for aggregate recalculation
        var affectedPlayerNames = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .Select(ps => ps.PlayerName)
            .Distinct()
            .ToListAsync();

        logger.LogInformation("Round {RoundId} affects {PlayerCount} players", roundId, affectedPlayerNames.Count);

        // Step 2: Get session IDs for this round (needed for observations deletion)
        var sessionIds = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .Select(ps => ps.SessionId)
            .ToListAsync();

        // Step 3: Delete PlayerAchievements where RoundId = roundId
        var deletedAchievements = await dbContext.PlayerAchievements
            .Where(pa => pa.RoundId == roundId)
            .ExecuteDeleteAsync();
        logger.LogInformation("Deleted {Count} achievements for round {RoundId}", deletedAchievements, roundId);

        // Step 4: Delete PlayerObservations where SessionId in sessions of round
        var deletedObservations = 0;
        if (sessionIds.Count > 0)
        {
            deletedObservations = await dbContext.PlayerObservations
                .Where(po => sessionIds.Contains(po.SessionId))
                .ExecuteDeleteAsync();
            logger.LogInformation("Deleted {Count} observations for round {RoundId}", deletedObservations, roundId);
        }

        // Step 5: Delete PlayerSessions where RoundId = roundId
        var deletedSessions = await dbContext.PlayerSessions
            .Where(ps => ps.RoundId == roundId)
            .ExecuteDeleteAsync();
        logger.LogInformation("Deleted {Count} sessions for round {RoundId}", deletedSessions, roundId);

        // Step 6: Delete the Round
        var deletedRounds = await dbContext.Rounds
            .Where(r => r.RoundId == roundId)
            .ExecuteDeleteAsync();
        logger.LogInformation("Deleted round {RoundId}", roundId);

        // Step 7: Create audit log entry
        var details = JsonSerializer.Serialize(new
        {
            DeletedAchievements = deletedAchievements,
            DeletedObservations = deletedObservations,
            DeletedSessions = deletedSessions,
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

        // Step 8: Queue aggregate recalculation for affected players
        if (affectedPlayerNames.Count > 0)
        {
            logger.LogInformation("Queueing aggregate recalculation for {PlayerCount} affected players", affectedPlayerNames.Count);
            _ = Task.Run(async () =>
            {
                try
                {
                    await aggregateBackfillService.RunForPlayersAsync(affectedPlayerNames);
                    logger.LogInformation("Aggregate recalculation completed for {PlayerCount} players after deleting round {RoundId}",
                        affectedPlayerNames.Count, roundId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to recalculate aggregates for players after deleting round {RoundId}", roundId);
                }
            });
        }

        return new DeleteRoundResponse(
            roundId,
            deletedAchievements,
            deletedObservations,
            deletedSessions,
            deletedRounds,
            affectedPlayerNames.Count
        );
    }

    public async Task<List<AdminAuditLog>> GetAuditLogAsync(int limit = 100)
    {
        return await dbContext.AdminAuditLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

}
