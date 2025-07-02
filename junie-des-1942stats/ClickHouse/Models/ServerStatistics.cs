using System.Runtime.Serialization;

namespace junie_des_1942stats.ClickHouse.Models;

[DataContract(Name = "ClickHouseServerStatistics")]
public class ServerStatistics
{
    public string MapName { get; set; } = "";
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int SessionsPlayed { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
} 