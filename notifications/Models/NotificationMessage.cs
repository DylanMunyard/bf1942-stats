namespace notifications.Models;

public class BuddyNotificationMessage
{
    public string Type { get; set; } = "";
    public string BuddyName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string? JoinLink { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
}
