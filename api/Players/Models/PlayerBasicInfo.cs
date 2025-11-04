namespace api.PlayerStats.Models;

public class PlayerBasicInfo
{
    public string PlayerName { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public ServerInfo? CurrentServer { get; set; }
}
