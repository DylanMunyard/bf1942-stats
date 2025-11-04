namespace api.Auth.Models;

public class PlayerInfoResponse
{
    public string Name { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public bool AiBot { get; set; }
    public bool IsOnline { get; set; }
    public string LastSeenIso { get; set; } = "";
    public string? CurrentServer { get; set; }
    public string? CurrentMap { get; set; }
    public int? CurrentSessionScore { get; set; }
    public int? CurrentSessionKills { get; set; }
    public int? CurrentSessionDeaths { get; set; }
}
