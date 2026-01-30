namespace api.Players.Models;

public class BestScoreDetail
{
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public string MapName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string RoundId { get; set; } = "";
}
