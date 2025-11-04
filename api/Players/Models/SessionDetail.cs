namespace api.PlayerStats.Models;

public class SessionDetail
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string PlayerName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public bool IsActive { get; set; }

    // Related entity details
    public PlayerDetailInfo PlayerDetails { get; set; } = new();
    public ServerDetailInfo? ServerDetails { get; set; }
    public List<ObservationInfo> Observations { get; set; } = new();
}
