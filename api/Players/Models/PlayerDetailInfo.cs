namespace api.PlayerStats.Models;

public class PlayerDetailInfo
{
    public string Name { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsAiBot { get; set; }
}
