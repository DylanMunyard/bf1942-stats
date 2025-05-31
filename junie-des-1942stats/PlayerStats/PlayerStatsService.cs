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
            .Select(p => new PlayerBasicInfo
            {
                PlayerName = p.Name,
                TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
                LastSeen = p.LastSeen,
                IsActive = p.Sessions.Any(s => s.IsActive),
                // Include the server info for active players
                CurrentServer = p.Sessions.Any(s => s.IsActive) ? 
                    p.Sessions.Where(s => s.IsActive)
                        .Select(s => new ServerInfo
                        {
                            ServerGuid = s.ServerGuid,
                            ServerName = s.Server.Name,
                            SessionKills = s.TotalKills,
                            SessionDeaths = s.TotalDeaths,
                            MapName = s.MapName
                        })
                        .FirstOrDefault() 
                    : null
            })
            .OrderByDescending(p => p.LastSeen)
            .ToListAsync();

        return players;
    }

    // Get total play time for a player (in minutes)
    public async Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName)
    {
        // Get recent sessions for the player
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .Include(ps => ps.Server)
            .OrderByDescending(ps => ps.LastSeenTime)
            .Take(10)
            .ToListAsync();
            
        if (sessions.Count == 0)
            return new PlayerTimeStatistics();
            
        var latestSession = sessions.OrderByDescending(s => s.LastSeenTime).FirstOrDefault();
        var now = DateTime.UtcNow;
        
        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = latestSession != null && 
                       (now - latestSession.LastSeenTime) <= _activeThreshold;
        
        // Get the list of servers the player was active on in the last 7 days
        var recentServers = sessions
            .Where(s => (now - s.LastSeenTime).TotalDays <= 7)
            .GroupBy(s => new { ServerGuid = s.ServerGuid, ServerName = s.Server.Name })
            .Select(g => new RecentServerActivity
            {
                ServerGuid = g.Key.ServerGuid,
                ServerName = g.Key.ServerName,
                TotalPlayTimeMinutes = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths),
                LastPlayed = g.Max(s => s.LastSeenTime)
            })
            .OrderByDescending(s => s.LastPlayed)
            .ToList();

        var bestSession = sessions.MaxBy(x => x.TotalScore);
        var stats = new PlayerTimeStatistics
        {
            TotalSessions = sessions.Count,
            TotalPlayTimeMinutes = sessions.Sum(s =>
                (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
            TotalObservations = sessions.Sum(s => s.ObservationCount),
            FirstPlayed = sessions.Min(s => s.StartTime),
            LastPlayed = sessions.Max(s => s.LastSeenTime),
            TotalKills = sessions.Sum(s => s.TotalKills),
            TotalDeaths = sessions.Sum(s => s.TotalDeaths),
            BestSession = bestSession != null
                ? new Session
                {
                    TotalScore = bestSession.TotalScore,
                    GameType = bestSession.GameType,
                    MapName = bestSession.MapName,
                    IsActive = bestSession.IsActive,
                    LastSeenTime = bestSession.LastSeenTime,
                    StartTime = bestSession.StartTime,
                    TotalDeaths = bestSession.TotalDeaths,
                    TotalKills = bestSession.TotalKills,
                    ServerName = bestSession.Server.Name,
                }
                : null,

            // New properties
            IsActive = isActive,
            CurrentServer = isActive
                ? new ServerInfo
                {
                    ServerGuid = latestSession!.ServerGuid,
                    ServerName = latestSession.Server.Name,
                    SessionKills = latestSession.TotalKills,
                    SessionDeaths = latestSession.TotalDeaths,
                    MapName = latestSession.MapName,
                }
                : null,
            RecentServers = recentServers
        };
            
        return stats;
    }
}