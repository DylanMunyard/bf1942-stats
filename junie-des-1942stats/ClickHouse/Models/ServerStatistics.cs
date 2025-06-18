namespace junie_des_1942stats.ClickHouse.Models;

public class ServerStatistics
{
    public string ServerName { get; set; }
    public string MapName { get; set; }
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int SessionsPlayed { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
} 