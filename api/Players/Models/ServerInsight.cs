namespace api.Players.Models;

public class ServerInsight
{
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string GameId { get; set; } = "";
    public double TotalMinutes { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int HighestScore { get; set; }
    public string HighestScoreRoundId { get; set; } = "";
    public string HighestScoreMapName { get; set; } = "";
    public DateTime HighestScoreStartTime { get; set; }
    public double KillsPerMinute { get; set; }
    public int TotalRounds { get; set; }
    public ServerRanking? Ranking { get; set; }
    public double KdRatio => TotalDeaths > 0 ? Math.Round((double)TotalKills / TotalDeaths, 2) : TotalKills;
}
