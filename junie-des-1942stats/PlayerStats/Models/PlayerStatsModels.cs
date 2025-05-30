﻿namespace junie_des_1942stats.PlayerStats.Models;

public class PlayerBasicInfo
{
    public string PlayerName { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}

public class PlayerTimeStatistics
{
    public int TotalSessions { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstPlayed { get; set; }
    public DateTime LastPlayed { get; set; }
    public int HighestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    
    // New properties
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
    public Session? BestSession { get; set; }
    public List<Session> RecentSessions { get; set; } = [];
}

public class ServerInfo
{
    public string ServerGuid { get; set; }
    public string ServerName { get; set; }
    public int SessionKills { get; set; }
    public int SessionDeaths { get; set; }
    public string MapName { get; set; }
}


public class Session
{
    public DateTime StartTime { get; set; }
    public DateTime LastSeenTime { get; set; }
    public bool IsActive { get; set; } // True if session is ongoing
    public int TotalScore { get; set; } // Can track highest score or final score
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public string ServerName { get; set; } = "";
}
