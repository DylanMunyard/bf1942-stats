namespace api.PlayerStats.Models;

public class ServerInfo
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int SessionKills { get; set; }
    public int SessionDeaths { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
}
