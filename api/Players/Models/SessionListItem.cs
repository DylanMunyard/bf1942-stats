namespace api.PlayerStats.Models;

public class SessionListItem
{
    public int SessionId { get; set; }
    public string? RoundId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public bool IsActive { get; set; }
}
