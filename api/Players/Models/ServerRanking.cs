namespace api.Players.Models;

public class ServerRanking
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int TotalScore { get; set; }
    public int TotalRankedPlayers { get; set; }
    public string RankDisplay => $"{Rank} of {TotalRankedPlayers}";
    public string ScoreDisplay => $"{TotalScore} points";

    public double AveragePing { get; set; }
}
