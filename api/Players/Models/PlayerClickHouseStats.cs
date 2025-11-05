namespace api.PlayerStats.Models;

public class PlayerClickHouseStats
{
    public string PlayerName { get; set; } = "";
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}
