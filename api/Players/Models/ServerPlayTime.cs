namespace api.Players.Models;

public class ServerPlayTime
{
    public string ServerGuid { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public int MinutesPlayed { get; set; }
}
