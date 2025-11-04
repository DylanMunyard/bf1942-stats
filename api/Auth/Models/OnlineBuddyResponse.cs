namespace api.Auth.Models;

public class OnlineBuddyResponse
{
    public string PlayerName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string? RoundId { get; set; }
    public string? CurrentMap { get; set; }
    public string? JoinLink { get; set; }
    public int SessionDurationMinutes { get; set; }
    public int CurrentScore { get; set; }
    public int CurrentKills { get; set; }
    public int CurrentDeaths { get; set; }
    public DateTime JoinedAt { get; set; }
}
