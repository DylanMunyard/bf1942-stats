using junie_des_1942stats.PlayerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.PlayerStats;

public class PlayerStatsService
{
    private readonly PlayerTrackerDbContext _dbContext;

    // Define a threshold for considering a player "active" (e.g., 5 minutes)
    private readonly TimeSpan _activeThreshold = TimeSpan.FromMinutes(5);

    public PlayerStatsService(PlayerTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PlayerBasicInfo>> GetAllPlayersBasicInfo()
    {
        var players = await _dbContext.Players
            .Where(p => !p.AiBot)
            .Select(p => new PlayerBasicInfo
            {
                PlayerName = p.Name,
                TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
                LastSeen = p.LastSeen,
                IsActive = p.Sessions.Any(s => s.IsActive),
                // Include the server info for active players
                CurrentServer = p.Sessions.Any(s => s.IsActive)
                    ? p.Sessions.Where(s => s.IsActive)
                        .Select(s => new ServerInfo
                        {
                            ServerGuid = s.ServerGuid,
                            ServerName = s.Server.Name,
                            SessionKills = s.TotalKills,
                            SessionDeaths = s.TotalDeaths,
                            MapName = s.MapName,
                            GameId = s.Server.GameId,
                        })
                        .FirstOrDefault()
                    : null
            })
            .OrderByDescending(p => p.LastSeen)
            .ToListAsync();

        return players;
    }

    public async Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName)
    {
        // First check if the player exists
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
            return new PlayerTimeStatistics();

        var now = DateTime.UtcNow;

        // Get aggregated stats directly from the database
        var aggregateStats = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                TotalSessions = g.Count(),
                FirstPlayed = g.Min(s => s.StartTime),
                LastPlayed = g.Max(s => s.LastSeenTime),
                HighestScore = g.Max(s => s.TotalScore),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .FirstOrDefaultAsync();

        if (aggregateStats == null)
            return new PlayerTimeStatistics();

        // Get the most recent 10 sessions with server info
        var recentSessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .OrderByDescending(s => s.LastSeenTime)
            .Include(s => s.Server)
            .Take(10)
            .Select(s => new Session
            {
                SessionId = s.SessionId,
                ServerName = s.Server.Name,
                MapName = s.MapName,
                GameType = s.GameType,
                StartTime = s.StartTime,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                TotalScore = s.TotalScore,
                IsActive = s.IsActive
            })
            .ToListAsync();

        // Get the best session (highest kills, then by score if tied)
        var bestSession = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .OrderByDescending(s => s.TotalScore)
            .Include(s => s.Server)
            .Select(s => new Session
            {
                SessionId = s.SessionId,
                ServerName = s.Server.Name,
                MapName = s.MapName,
                GameType = s.GameType,
                StartTime = s.StartTime,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                TotalScore = s.TotalScore,
                IsActive = s.IsActive
            })
            .FirstOrDefaultAsync();

        // Get the current active session if any
        var activeSession = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.IsActive)
            .Include(s => s.Server)
            .OrderByDescending(s => s.LastSeenTime)
            .FirstOrDefaultAsync();

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null &&
                        (now - activeSession.LastSeenTime) <= _activeThreshold;
        
        var insights = await GetPlayerInsights(playerName);

        var stats = new PlayerTimeStatistics
        {
            TotalSessions = aggregateStats.TotalSessions,
            TotalPlayTimeMinutes = player.TotalPlayTimeMinutes,
            FirstPlayed = aggregateStats.FirstPlayed,
            LastPlayed = aggregateStats.LastPlayed,
            HighestScore = aggregateStats.HighestScore,
            TotalKills = aggregateStats.TotalKills,
            TotalDeaths = aggregateStats.TotalDeaths,

            IsActive = isActive,
            CurrentServer = isActive && activeSession != null
                ? new ServerInfo
                {
                    ServerGuid = activeSession.ServerGuid,
                    ServerName = activeSession.Server.Name,
                    SessionKills = activeSession.TotalKills,
                    SessionDeaths = activeSession.TotalDeaths,
                    GameId = activeSession.Server.GameId,
                    MapName = activeSession.MapName,
                }
                : null,
            RecentSessions = recentSessions,
            BestSession = bestSession,
            Insights = insights
        };

        return stats;
    }

    public async Task<PagedResult<SessionListItem>> GetPlayerSessions(
        string playerName, 
        int page = 1, 
        int pageSize = 100)
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
        
        // Count total sessions for this player (for pagination metadata)
        var totalCount = await _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName)
            .CountAsync();
    
        // Get aggregate player stats
        var aggregateStats = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                TotalSessions = g.Count(),
                FirstPlayed = g.Min(s => s.StartTime),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .FirstOrDefaultAsync();
    
        // Get the specified page of sessions
        var sessions = await _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName)
            .OrderByDescending(s => s.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SessionListItem
            {
                SessionId = s.SessionId,
                ServerName = s.Server.Name,
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

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null && 
                   (DateTime.UtcNow - activeSession.LastSeenTime) <= _activeThreshold;

        // Return paged result with metadata and player context
        return new PagedResult<SessionListItem>
        {
            Items = sessions,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            PlayerInfo = new PlayerContextInfo
            {
                Name = player.Name,
                TotalPlayTimeMinutes = player.TotalPlayTimeMinutes,
                FirstSeen = aggregateStats?.FirstPlayed ?? player.FirstSeen,
                LastSeen = player.LastSeen,
                IsActive = isActive,
                TotalSessions = aggregateStats?.TotalSessions ?? 0,
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
            }
        };
    }

    public async Task<SessionDetail?> GetSession(string playerName, int sessionId)
    {
        var session = await _dbContext.PlayerSessions
            .Where(s => s.SessionId == sessionId && s.PlayerName == playerName)
            .Include(s => s.Player)
            .Include(s => s.Server)
            .Include(s => s.Observations)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return null;
        }

        var sessionDetail = new SessionDetail
        {
            SessionId = session.SessionId,
            PlayerName = session.PlayerName,
            ServerName = session.Server.Name,
            MapName = session.MapName,
            GameType = session.GameType,
            StartTime = session.StartTime,
            EndTime = session.IsActive ? null : session.LastSeenTime,
            TotalPlayTimeMinutes = (int)Math.Ceiling((session.LastSeenTime - session.StartTime).TotalMinutes),
            TotalKills = session.TotalKills,
            TotalDeaths = session.TotalDeaths,
            TotalScore = session.TotalScore,
            IsActive = session.IsActive,

            // Player details
            PlayerDetails = new PlayerDetailInfo
            {
                Name = session.Player.Name,
                TotalPlayTimeMinutes = session.Player.TotalPlayTimeMinutes,
                FirstSeen = session.Player.FirstSeen,
                LastSeen = session.Player.LastSeen,
                IsAiBot = session.Player.AiBot
            },

            // Server details
            ServerDetails = new ServerDetailInfo
            {
                Guid = session.Server.Guid,
                Name = session.Server.Name,
                Address = session.Server.Ip,
                Port = session.Server.Port,
                GameId = session.Server.GameId
            },

            // Observations over time
            Observations = session.Observations.Select(o => new ObservationInfo
            {
                Timestamp = o.Timestamp,
                Score = o.Score,
                Kills = o.Kills,
                Deaths = o.Deaths,
                Ping = o.Ping,
                TeamLabel = o.TeamLabel
            }).ToList(),

        };

        return sessionDetail;
    }

    public async Task<PlayerInsights> GetPlayerInsights(
        string playerName, 
        DateTime? startDate = null, 
        DateTime? endDate = null, 
        int? daysToAnalyze = null)
    {
        // Calculate the time period
        var endPeriod = endDate ?? DateTime.UtcNow;
        DateTime startPeriod;
        
        if (startDate.HasValue)
        {
            startPeriod = startDate.Value;
        }
        else if (daysToAnalyze.HasValue)
        {
            startPeriod = endPeriod.AddDays(-daysToAnalyze.Value);
        }
        else
        {
            // Default to 1 week
            startPeriod = endPeriod.AddDays(-7);
        }
        
        // Check if the player exists
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
            return new PlayerInsights { PlayerName = playerName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Get player sessions within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.StartTime >= startPeriod && ps.LastSeenTime <= endPeriod)
            .Include(s => s.Server)
            .ToListAsync();

        var insights = new PlayerInsights
        {
            PlayerName = playerName,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // 1. Calculate time spent on each server
        var serverPlayTimes = sessions
            .GroupBy(s => new { s.ServerGuid, ServerName = s.Server.Name })
            .Select(g => new ServerPlayTime
            {
                ServerGuid = g.Key.ServerGuid,
                ServerName = g.Key.ServerName,
                MinutesPlayed = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes))
            })
            .OrderByDescending(s => s.MinutesPlayed)
            .ToList();
        
        insights.ServerPlayTimes = serverPlayTimes;

        // 2. Calculate favorite maps by time played with additional stats
        var mapPlayTimes = sessions
            .GroupBy(s => s.MapName)
            .Select(g => new MapPlayTime
            {
                MapName = g.Key,
                MinutesPlayed = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths),
                KDRatio = g.Sum(s => s.TotalDeaths) > 0 
                    ? Math.Round((double)g.Sum(s => s.TotalKills) / g.Sum(s => s.TotalDeaths), 2) 
                    : g.Sum(s => s.TotalKills) // If no deaths, KDR equals total kills
            })
            .OrderByDescending(m => m.MinutesPlayed)
            .ToList();
        
        insights.FavoriteMaps = mapPlayTimes;

        // 3. Calculate activity by hour (when they're usually online)
        // Initialize hourly activity tracker
        var hourlyActivity = new Dictionary<int, int>();
        for (int hour = 0; hour < 24; hour++)
        {
            hourlyActivity[hour] = 0;
        }

        // Process each session's time range and break into hourly chunks
        foreach (var session in sessions)
        {
            var sessionStart = session.StartTime;
            var sessionEnd = session.LastSeenTime;
            
            // Track activity by processing continuous blocks of time
            var currentTime = sessionStart;
            
            while (currentTime < sessionEnd)
            {
                int hour = currentTime.Hour;
                
                // Calculate how much time was spent in this hour
                // Either go to the end of the current hour or the end of the session, whichever comes first
                var hourEnd = new DateTime(
                    currentTime.Year, 
                    currentTime.Month, 
                    currentTime.Day, 
                    hour, 
                    59, 
                    59, 
                    999);
            
                if (hourEnd > sessionEnd)
                {
                    hourEnd = sessionEnd;
                }
            
                // Add the minutes spent in this hour
                int minutesInHour = (int)Math.Ceiling((hourEnd - currentTime).TotalMinutes);
                hourlyActivity[hour] += minutesInHour;
            
                // Move to the next hour
                currentTime = hourEnd.AddMilliseconds(1);
            }
        }

        insights.ActivityByHour = hourlyActivity
            .Select(kvp => new HourlyActivity { Hour = kvp.Key, MinutesActive = kvp.Value })
            .OrderByDescending(ha => ha.MinutesActive)
            .ToList();

        return insights;
    }
}