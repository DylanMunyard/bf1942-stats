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
        var sessionQuery = _dbContext.PlayerSessions
            .Include(s => s.Server)
            .AsQueryable();

        if (!string.IsNullOrEmpty(serverGuid))
        {
            sessionQuery = sessionQuery.Where(s => s.ServerGuid == serverGuid);
        }

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.ServerName))
            {
                sessionQuery = sessionQuery.Where(s => EF.Functions.Like(s.Server.Name, $"%{filters.ServerName}%"));
            }

            if (!string.IsNullOrEmpty(filters.ServerGuid))
            {
                sessionQuery = sessionQuery.Where(s => s.ServerGuid == filters.ServerGuid);
            }

            if (!string.IsNullOrEmpty(filters.MapName))
            {
                sessionQuery = sessionQuery.Where(s => EF.Functions.Like(s.MapName, $"%{filters.MapName}%"));
            }

            if (!string.IsNullOrEmpty(filters.GameType))
            {
                sessionQuery = sessionQuery.Where(s => s.GameType == filters.GameType);
            }

            if (!string.IsNullOrEmpty(filters.GameId))
            {
                sessionQuery = sessionQuery.Where(s => s.Server.GameId == filters.GameId);
            }

            if (filters.StartTimeFrom.HasValue)
            {
                sessionQuery = sessionQuery.Where(s => s.StartTime >= filters.StartTimeFrom.Value);
            }

            if (filters.StartTimeTo.HasValue)
            {
                sessionQuery = sessionQuery.Where(s => s.StartTime <= filters.StartTimeTo.Value);
            }

        }

        var roundsQuery = sessionQuery
            .GroupBy(s => new { s.ServerGuid, s.MapName, s.StartTime.Date, s.StartTime.Hour })
            .Select(g => new RoundListItem
            {
                RoundId = g.Key.ServerGuid + "_" + g.Key.MapName + "_" + g.Min(s => s.StartTime).ToString("yyyy-MM-dd_HH-mm-ss"),
                ServerName = g.First().Server.Name,
                ServerGuid = g.Key.ServerGuid,
                MapName = g.Key.MapName,
                GameType = g.First().GameType,
                StartTime = g.Min(s => s.StartTime),
                EndTime = g.Max(s => s.IsActive) ? DateTime.UtcNow : g.Max(s => s.LastSeenTime),
                DurationMinutes = g.Max(s => s.IsActive) 
                    ? (int)(DateTime.UtcNow - g.Min(s => s.StartTime)).TotalMinutes
                    : (int)(g.Max(s => s.LastSeenTime) - g.Min(s => s.StartTime)).TotalMinutes,
                ParticipantCount = g.Select(s => s.PlayerName).Distinct().Count(),
                TotalSessions = g.Count(),
                IsActive = g.Max(s => s.IsActive),
                TotalScore = g.Sum(s => s.TotalScore),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            });

        if (filters != null)
        {
            if (filters.MinDuration.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.DurationMinutes >= filters.MinDuration.Value);
            }

            if (filters.MaxDuration.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.DurationMinutes <= filters.MaxDuration.Value);
            }

            if (filters.MinParticipants.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.ParticipantCount >= filters.MinParticipants.Value);
            }

            if (filters.MaxParticipants.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.ParticipantCount <= filters.MaxParticipants.Value);
            }

            if (filters.EndTimeFrom.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.EndTime >= filters.EndTimeFrom.Value);
            }

            if (filters.EndTimeTo.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.EndTime <= filters.EndTimeTo.Value);
            }

            if (filters.IsActive.HasValue)
            {
                roundsQuery = roundsQuery.Where(r => r.IsActive == filters.IsActive.Value);
            }
        }

        return roundsQuery;
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