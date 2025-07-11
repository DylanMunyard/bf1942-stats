using PlayerStatsModels = junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.ServerStats;

public class HistoricalRoundsService(PlayerTrackerDbContext dbContext)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;


    public async Task<PlayerStatsModels.PagedResult<RoundListItem>> GetAllRounds(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        RoundFilters? filters = null)
    {
        var roundsQuery = BuildRoundsQuery(null, filters);

        var totalItems = await roundsQuery.CountAsync();

        var rounds = await roundsQuery
            .OrderBy(sortBy, sortOrder)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PlayerStatsModels.PagedResult<RoundListItem>
        {
            Items = rounds,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        };
    }


    private IQueryable<RoundListItem> BuildRoundsQuery(string? serverGuid, RoundFilters? filters)
    {
        var sql = @"
            WITH SessionsWithGaps AS (
                SELECT 
                    ps.ServerGuid,
                    ps.MapName,
                    ps.GameType,
                    ps.PlayerName,
                    ps.StartTime,
                    ps.LastSeenTime,
                    ps.IsActive,
                    gs.Name as ServerName,
                    LAG(ps.StartTime) OVER (PARTITION BY ps.ServerGuid, ps.MapName ORDER BY ps.StartTime) as PrevStartTime,
                    CASE 
                        WHEN LAG(ps.StartTime) OVER (PARTITION BY ps.ServerGuid, ps.MapName ORDER BY ps.StartTime) IS NULL 
                        OR (strftime('%s', ps.StartTime) - strftime('%s', LAG(ps.StartTime) OVER (PARTITION BY ps.ServerGuid, ps.MapName ORDER BY ps.StartTime))) > 600 
                        THEN 1 
                        ELSE 0 
                    END as IsNewRound
                FROM PlayerSessions ps
                INNER JOIN Servers gs ON ps.ServerGuid = gs.Guid
                WHERE 1=1";

        var parameters = new List<object>();
        var paramIndex = 0;

        if (!string.IsNullOrEmpty(serverGuid))
        {
            sql += $" AND ps.ServerGuid = {{{paramIndex}}}";
            parameters.Add(serverGuid);
            paramIndex++;
        }

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.ServerName))
            {
                sql += $" AND gs.Name LIKE {{{paramIndex}}}";
                parameters.Add($"%{filters.ServerName}%");
                paramIndex++;
            }

            if (!string.IsNullOrEmpty(filters.ServerGuid))
            {
                sql += $" AND ps.ServerGuid = {{{paramIndex}}}";
                parameters.Add(filters.ServerGuid);
                paramIndex++;
            }

            if (!string.IsNullOrEmpty(filters.MapName))
            {
                sql += $" AND ps.MapName LIKE {{{paramIndex}}}";
                parameters.Add($"%{filters.MapName}%");
                paramIndex++;
            }

            if (!string.IsNullOrEmpty(filters.GameType))
            {
                sql += $" AND ps.GameType = {{{paramIndex}}}";
                parameters.Add(filters.GameType);
                paramIndex++;
            }

            if (!string.IsNullOrEmpty(filters.GameId))
            {
                sql += $" AND gs.GameId = {{{paramIndex}}}";
                parameters.Add(filters.GameId);
                paramIndex++;
            }

            if (filters.StartTimeFrom.HasValue)
            {
                sql += $" AND ps.StartTime >= {{{paramIndex}}}";
                parameters.Add(filters.StartTimeFrom.Value);
                paramIndex++;
            }

            if (filters.StartTimeTo.HasValue)
            {
                sql += $" AND ps.StartTime <= {{{paramIndex}}}";
                parameters.Add(filters.StartTimeTo.Value);
                paramIndex++;
            }
        }

        sql += @"
            ),
            SessionsWithRoundGroups AS (
                SELECT 
                    s.*,
                    SUM(s.IsNewRound) OVER (PARTITION BY s.ServerGuid, s.MapName ORDER BY s.StartTime ROWS UNBOUNDED PRECEDING) as RoundGroup
                FROM SessionsWithGaps s
            ),
            RoundGroups AS (
                SELECT 
                    s.ServerGuid,
                    s.MapName,
                    s.GameType,
                    s.ServerName,
                    s.RoundGroup,
                    MIN(s.StartTime) as StartTime,
                    CASE 
                        WHEN MAX(CASE WHEN s.IsActive = 1 THEN 1 ELSE 0 END) = 1 THEN datetime('now')
                        ELSE MAX(s.LastSeenTime)
                    END as EndTime,
                    COUNT(DISTINCT s.PlayerName) as ParticipantCount,
                    COUNT(*) as TotalSessions,
                    MAX(CASE WHEN s.IsActive = 1 THEN 1 ELSE 0 END) as IsActive
                FROM SessionsWithRoundGroups s
                GROUP BY s.ServerGuid, s.MapName, s.GameType, s.ServerName, s.RoundGroup
            )
            SELECT 
                ServerGuid || '_' || MapName || '_' || RoundGroup || '_' || strftime('%Y-%m-%d_%H-%M-%S', StartTime) as RoundId,
                ServerName,
                ServerGuid,
                MapName,
                GameType,
                StartTime,
                EndTime,
                cast((strftime('%s', EndTime) - strftime('%s', StartTime)) / 60.0 as integer) as DurationMinutes,
                ParticipantCount,
                TotalSessions,
                IsActive
            FROM RoundGroups
            ORDER BY EndTime DESC";

        if (filters != null)
        {
            if (filters.MinDuration.HasValue)
            {
                sql += $" AND cast((strftime('%s', EndTime) - strftime('%s', StartTime)) / 60.0 as integer) >= {{{paramIndex}}}";
                parameters.Add(filters.MinDuration.Value);
                paramIndex++;
            }

            if (filters.MaxDuration.HasValue)
            {
                sql += $" AND cast((strftime('%s', EndTime) - strftime('%s', StartTime)) / 60.0 as integer) <= {{{paramIndex}}}";
                parameters.Add(filters.MaxDuration.Value);
                paramIndex++;
            }

            if (filters.MinParticipants.HasValue)
            {
                sql += $" AND ParticipantCount >= {{{paramIndex}}}";
                parameters.Add(filters.MinParticipants.Value);
                paramIndex++;
            }

            if (filters.MaxParticipants.HasValue)
            {
                sql += $" AND ParticipantCount <= {{{paramIndex}}}";
                parameters.Add(filters.MaxParticipants.Value);
                paramIndex++;
            }

            if (filters.EndTimeFrom.HasValue)
            {
                sql += $" AND EndTime >= {{{paramIndex}}}";
                parameters.Add(filters.EndTimeFrom.Value);
                paramIndex++;
            }

            if (filters.EndTimeTo.HasValue)
            {
                sql += $" AND EndTime <= {{{paramIndex}}}";
                parameters.Add(filters.EndTimeTo.Value);
                paramIndex++;
            }

            if (filters.IsActive.HasValue)
            {
                sql += $" AND IsActive = {{{paramIndex}}}";
                parameters.Add(filters.IsActive.Value ? 1 : 0);
                paramIndex++;
            }
        }

        return _dbContext.Set<RoundListItem>().FromSqlRaw(sql, parameters.ToArray());
    }

}

public static class QueryableExtensions
{
    public static IQueryable<T> OrderBy<T>(this IQueryable<T> source, string sortBy, string sortOrder)
    {
        var property = typeof(T).GetProperty(sortBy);
        if (property == null)
            return source;

        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        var propertyAccess = System.Linq.Expressions.Expression.MakeMemberAccess(parameter, property);
        var orderByExpression = System.Linq.Expressions.Expression.Lambda(propertyAccess, parameter);

        var methodName = sortOrder.ToLower() == "desc" ? "OrderByDescending" : "OrderBy";
        var resultExpression = System.Linq.Expressions.Expression.Call(
            typeof(Queryable),
            methodName,
            new Type[] { typeof(T), property.PropertyType },
            source.Expression,
            System.Linq.Expressions.Expression.Quote(orderByExpression));

        return source.Provider.CreateQuery<T>(resultExpression);
    }
}