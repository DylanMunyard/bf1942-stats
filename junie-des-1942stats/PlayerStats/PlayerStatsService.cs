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
    // Get all sessions for this player
    var sessions = await _dbContext.PlayerSessions
        .Where(ps => ps.PlayerName == playerName)
        .Include(ps => ps.Server)
        .OrderByDescending(s => s.LastSeenTime)
        .Take(10)  // Get only the last 10 sessions
        .ToListAsync();
        
    if (sessions.Count == 0)
        return new PlayerTimeStatistics();
        
    var latestSession = sessions.FirstOrDefault();
    var now = DateTime.UtcNow;
    
    // Get all sessions for stats calculations (not limited to 10)
    var allSessions = await _dbContext.PlayerSessions
        .Where(ps => ps.PlayerName == playerName)
        .Include(playerSession => playerSession.Server)
        .ToListAsync();
    
    // Calculate best session directly in the database query
    var bestSession = await _dbContext.PlayerSessions
        .Where(ps => ps.PlayerName == playerName)
        .OrderByDescending(s => s.TotalKills)  // First ordering criteria: kills
        .ThenByDescending(s => s.TotalScore)   // Second ordering criteria: score
        .Include(s => s.Server)
        .FirstOrDefaultAsync();
    
    // Check if player is currently active (seen within the last 5 minutes)
    bool isActive = latestSession != null && 
                   (now - latestSession.LastSeenTime) <= _activeThreshold;
    
    // Get the list of servers the player was active on in the last 7 days
    var recentServers = allSessions
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
        
    // Map the database sessions to the model Session objects
    var recentSessionsModels = sessions.Select(s => new Session
    {
        ServerName = s.Server?.Name ?? "Unknown Server",
        MapName = s.MapName,
        GameType = s.GameType,
        StartTime = s.StartTime,
        TotalKills = s.TotalKills,
        TotalDeaths = s.TotalDeaths,
        TotalScore = s.TotalScore,
        IsActive = s.IsActive
    }).ToList();
    
    // Create a Session object for the best session if found
    Session? bestSessionModel = null;
    if (bestSession != null)
    {
        bestSessionModel = new Session
        {
            SessionId = bestSession.SessionId,
            ServerName = bestSession.Server?.Name ?? "Unknown Server",
            MapName = bestSession.MapName,
            GameType = bestSession.GameType,
            StartTime = bestSession.StartTime,
            EndTime = bestSession.IsActive ? null : bestSession.LastSeenTime,
            TotalKills = bestSession.TotalKills,
            TotalDeaths = bestSession.TotalDeaths,
            TotalScore = bestSession.TotalScore,
            IsActive = bestSession.IsActive
        };
    }
    
    var stats = new PlayerTimeStatistics
    {
        TotalSessions = allSessions.Count,
        TotalPlayTimeMinutes = allSessions.Sum(s => 
            (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
        TotalObservations = allSessions.Sum(s => s.ObservationCount),
        FirstPlayed = allSessions.Min(s => s.StartTime),
        LastPlayed = allSessions.Max(s => s.LastSeenTime),
        HighestScore = allSessions.Max(s => s.TotalScore),
        TotalKills = allSessions.Sum(s => s.TotalKills),
        TotalDeaths = allSessions.Sum(s => s.TotalDeaths),
        
        // New properties
        IsActive = isActive,
        CurrentServer = isActive ? new ServerInfo
        {
            ServerGuid = latestSession!.ServerGuid,
            ServerName = latestSession.Server?.Name ?? "Unknown Server",
            SessionKills = latestSession.TotalKills,
            SessionDeaths = latestSession.TotalDeaths
        } : null,
        RecentServers = recentServers,
        RecentSessions = recentSessionsModels,
        BestSession = bestSessionModel
    };
        
    return stats;
}
}