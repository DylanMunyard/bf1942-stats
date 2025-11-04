namespace api.PlayerStats.Models;

public class MapKillStats
{
    public string MapName { get; set; } = string.Empty;
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KdRatio => TotalDeaths > 0 ? Math.Round((double)TotalKills / TotalDeaths, 2) : TotalKills;
}
