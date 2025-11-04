namespace api.PlayerStats.Models;

public class BestScoreDetail
{
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string RoundId { get; set; } = string.Empty;
}
