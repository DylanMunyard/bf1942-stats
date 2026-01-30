namespace api.Players.Models;

/// <summary>
/// Keyless entity type for raw SQL query results when retrieving server best scores.
/// </summary>
public class ServerBestScoreRaw
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public int BestScore { get; set; }
    public DateTime BestScoreDate { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int PlayTimeMinutes { get; set; }
    public int SessionId { get; set; }
}
