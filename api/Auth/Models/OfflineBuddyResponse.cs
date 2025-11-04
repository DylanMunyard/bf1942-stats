namespace api.Auth.Models;

public class OfflineBuddyResponse
{
    public string PlayerName { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public string LastSeenIso { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime AddedAt { get; set; }
}
