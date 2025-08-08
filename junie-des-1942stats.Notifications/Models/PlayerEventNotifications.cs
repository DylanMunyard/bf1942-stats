namespace junie_des_1942stats.Notifications.Models;

public abstract record PlayerEventNotification
{
    public string PlayerName { get; init; } = "";
    public string ServerGuid { get; init; } = "";
    public string ServerName { get; init; } = "";
    public DateTime Timestamp { get; init; }
}

public record PlayerOnlineNotification : PlayerEventNotification
{
    public int SessionId { get; init; }
    public string MapName { get; init; } = "";
    public string GameType { get; init; } = "";
}

public record MapChangeNotification : PlayerEventNotification
{
    public string OldMapName { get; init; } = "";
    public string NewMapName { get; init; } = "";
    public int SessionId { get; init; }
}