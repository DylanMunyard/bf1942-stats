namespace junie_des_1942stats.ClickHouse.Models;

public class PlayerRound
{
    public string PlayerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTime RoundStartTime { get; set; }
    public DateTime RoundEndTime { get; set; }
    public int FinalScore { get; set; }
    public uint FinalKills { get; set; }
    public uint FinalDeaths { get; set; }
    public double PlayTimeMinutes { get; set; }
    public bool IsBot { get; set; }

    // Convenience properties for gamification (map to existing properties)
    public int Score => FinalScore;
    public int Kills => (int)FinalKills;
    public int Deaths => (int)FinalDeaths;
    public string RoundId { get; set; } = "";
    public string TeamLabel { get; set; } = "";
    public string GameId { get; set; } = "";
    public string Game { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public double? AveragePing { get; set; }
}