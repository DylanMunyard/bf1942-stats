namespace api.PlayerStats.Models;

// New class to hold player context information
public class PlayerContextInfo
{
    public string Name { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}
