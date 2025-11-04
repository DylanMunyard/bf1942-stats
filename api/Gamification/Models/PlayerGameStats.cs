namespace api.Gamification.Models;

public class PlayerGameStats
{
    public string PlayerName { get; set; } = "";
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastUpdated { get; set; }
}
