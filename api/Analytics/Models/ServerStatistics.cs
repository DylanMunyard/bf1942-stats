using System.Runtime.Serialization;

namespace api.Analytics.Models;

[DataContract(Name = "ServerStatistics")]
public class ServerStatistics
{
    public string MapName { get; set; } = "";
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int SessionsPlayed { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}
