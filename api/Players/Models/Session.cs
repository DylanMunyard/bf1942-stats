namespace api.Players.Models;

public class Session
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string? GameType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastSeenTime { get; set; }
    public bool IsActive { get; set; } // True if session is ongoing
    public int TotalScore { get; set; } // Can track highest score or final score
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public string GameId { get; set; } = string.Empty;
}
