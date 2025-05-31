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
        CurrentServer = isActive && activeSession != null ? new ServerInfo
        {
            ServerGuid = activeSession.ServerGuid,
            ServerName = activeSession.Server.Name,
            SessionKills = activeSession.TotalKills,
            SessionDeaths = activeSession.TotalDeaths
        } : null,
        RecentSessions = recentSessions,
        BestSession = bestSession
    };
    
    return stats;
}
}