using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.PlayerStats;

public class SessionsService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<SessionsService> _logger;

    // Define a threshold for considering a player "active" (e.g., 1 minute)
    private readonly TimeSpan _activeThreshold = TimeSpan.FromMinutes(1);

    public SessionsService(PlayerTrackerDbContext dbContext, ILogger<SessionsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedResult<SessionListItem>> GetSessions(
        int page = 1,
        int pageSize = 100,
        string sortBy = "StartTime",
        string sortOrder = "desc",
        PlayerFilters? filters = null)
    {
        // Build base query for all sessions
        var baseSessionQuery = _dbContext.PlayerSessions.AsQueryable();

        // Apply filters if provided
        if (filters != null)
        {
            // Player name filter (optional now)
            if (!string.IsNullOrEmpty(filters.PlayerName))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.PlayerName == filters.PlayerName);
            }

            if (filters.LastSeenFrom.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.LastSeenTime >= filters.LastSeenFrom.Value);
            }

            if (filters.LastSeenTo.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.LastSeenTime <= filters.LastSeenTo.Value);
            }

            if (filters.StartTimeFrom.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.StartTime >= filters.StartTimeFrom.Value);
            }

            if (filters.StartTimeTo.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.StartTime <= filters.StartTimeTo.Value);
            }

            if (filters.IsActive.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.IsActive == filters.IsActive.Value);
            }

            // Server-related filters
            if (!string.IsNullOrEmpty(filters.ServerName))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.Server.Name.Contains(filters.ServerName));
            }

            if (!string.IsNullOrEmpty(filters.ServerGuid))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.ServerGuid == filters.ServerGuid);
            }

            if (!string.IsNullOrEmpty(filters.GameId))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.Server.GameId == filters.GameId);
            }

            // Map and game type filters
            if (!string.IsNullOrEmpty(filters.MapName))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.MapName.Contains(filters.MapName));
            }

            if (!string.IsNullOrEmpty(filters.GameType))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.GameType != null && s.GameType.Contains(filters.GameType));
            }

            // Duration filters
            if (filters.MinPlayTime.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s =>
                    (s.LastSeenTime - s.StartTime).TotalMinutes >= filters.MinPlayTime.Value);
            }

            if (filters.MaxPlayTime.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s =>
                    (s.LastSeenTime - s.StartTime).TotalMinutes <= filters.MaxPlayTime.Value);
            }

            // Score filters
            if (filters.MinScore.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalScore >= filters.MinScore.Value);
            }

            if (filters.MaxScore.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalScore <= filters.MaxScore.Value);
            }

            // Kills filters
            if (filters.MinKills.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalKills >= filters.MinKills.Value);
            }

            if (filters.MaxKills.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalKills <= filters.MaxKills.Value);
            }

            // Deaths filters
            if (filters.MinDeaths.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalDeaths >= filters.MinDeaths.Value);
            }

            if (filters.MaxDeaths.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalDeaths <= filters.MaxDeaths.Value);
            }
        }

        // Count total sessions (for pagination metadata, after filters applied)
        var totalCount = await baseSessionQuery.CountAsync();

        // Apply sorting
        var isDescending = sortOrder.ToLower() == "desc";

        var sortedQuery = sortBy.ToLower() switch
        {
            "sessionid" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.SessionId)
                : baseSessionQuery.OrderBy(s => s.SessionId),
            "playername" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.PlayerName)
                : baseSessionQuery.OrderBy(s => s.PlayerName),
            "servername" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.Server.Name)
                : baseSessionQuery.OrderBy(s => s.Server.Name),
            "mapname" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.MapName)
                : baseSessionQuery.OrderBy(s => s.MapName),
            "gametype" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.GameType)
                : baseSessionQuery.OrderBy(s => s.GameType),
            "starttime" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.StartTime)
                : baseSessionQuery.OrderBy(s => s.StartTime),
            "endtime" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.LastSeenTime)
                : baseSessionQuery.OrderBy(s => s.LastSeenTime),
            "durationminutes" => isDescending
                ? baseSessionQuery.OrderByDescending(s => (s.LastSeenTime - s.StartTime).TotalMinutes)
                : baseSessionQuery.OrderBy(s => (s.LastSeenTime - s.StartTime).TotalMinutes),
            "score" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.TotalScore)
                : baseSessionQuery.OrderBy(s => s.TotalScore),
            "kills" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.TotalKills)
                : baseSessionQuery.OrderBy(s => s.TotalKills),
            "deaths" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.TotalDeaths)
                : baseSessionQuery.OrderBy(s => s.TotalDeaths),
            "isactive" => isDescending
                ? baseSessionQuery.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.StartTime)
                : baseSessionQuery.OrderBy(s => s.IsActive).ThenByDescending(s => s.StartTime),
            _ => baseSessionQuery.OrderByDescending(s => s.StartTime) // Default sorting
        };

        // Get the specified page of sessions (with filters and sorting applied)
        var sessions = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SessionListItem
            {
                SessionId = s.SessionId,
                RoundId = s.RoundId,
                PlayerName = s.PlayerName,
                ServerName = s.Server.Name,
                ServerGuid = s.ServerGuid,
                MapName = s.MapName,
                GameType = s.GameType,
                StartTime = s.StartTime,
                EndTime = s.LastSeenTime,
                DurationMinutes = (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes),
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                IsActive = s.IsActive
            })
            .ToListAsync();

        // Return paged result without player context (since this is general sessions)
        return new PagedResult<SessionListItem>
        {
            Items = sessions,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            PlayerInfo = null // No specific player context for general sessions
        };
    }

    public async Task<PagedResult<SessionListItem>> GetPlayerSessions(
        string playerName,
        int page = 1,
        int pageSize = 100,
        string sortBy = "StartTime",
        string sortOrder = "desc",
        PlayerFilters? filters = null)
    {
        // Get player information
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
        {
            return new PagedResult<SessionListItem>
            {
                Items = new List<SessionListItem>(),
                Page = page,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0
            };
        }

        // Get active session if any
        var activeSession = await _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName && s.IsActive)
            .Include(s => s.Server)
            .FirstOrDefaultAsync();

        // Set player name filter and call the general sessions method
        if (filters == null)
            filters = new PlayerFilters();
        
        filters.PlayerName = playerName;

        var result = await GetSessions(page, pageSize, sortBy, sortOrder, filters);

        // Get aggregate player stats for player context
        var aggregateStats = await _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName)
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                FirstPlayed = g.Min(s => s.StartTime),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .FirstOrDefaultAsync();

        // Check if player is currently active
        bool isActive = activeSession != null &&
                       (DateTime.UtcNow - activeSession.LastSeenTime) <= _activeThreshold;

        // Add player context info
        result.PlayerInfo = new PlayerContextInfo
        {
            Name = player.Name,
            TotalPlayTimeMinutes = player.TotalPlayTimeMinutes,
            FirstSeen = aggregateStats?.FirstPlayed ?? player.FirstSeen,
            LastSeen = player.LastSeen,
            IsActive = isActive,
            TotalKills = aggregateStats?.TotalKills ?? 0,
            TotalDeaths = aggregateStats?.TotalDeaths ?? 0,
            CurrentServer = isActive && activeSession != null
                ? new ServerInfo
                {
                    ServerGuid = activeSession.ServerGuid,
                    ServerName = activeSession.Server.Name,
                    SessionKills = activeSession.TotalKills,
                    SessionDeaths = activeSession.TotalDeaths,
                    MapName = activeSession.MapName,
                    GameId = activeSession.Server.GameId
                }
                : null
        };

        return result;
    }
}