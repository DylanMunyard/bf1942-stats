namespace api.PlayerStats.Models;

public class ServerBestScore
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int BestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int PlayTimeMinutes { get; set; }
    public DateTime BestScoreDate { get; set; }
    public string MapName { get; set; } = string.Empty;
    public int SessionId { get; set; }
}
