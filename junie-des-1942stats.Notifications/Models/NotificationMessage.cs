namespace junie_des_1942stats.Notifications.Models;

public class BuddyNotificationMessage
{
    public string Type { get; set; } = "";
    public string BuddyName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
}