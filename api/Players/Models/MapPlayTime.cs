namespace api.Players.Models;

public class MapPlayTime
{
    public string MapName { get; set; } = string.Empty;
    public int MinutesPlayed { get; set; }
    public double KDRatio { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalKills { get; set; }
}
