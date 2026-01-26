namespace api.DiscordNotifications.Models;

/// <summary>
/// Data for a suspicious round alert notification.
/// </summary>
public record SuspiciousRoundAlert(
    string RoundId,
    string MapName,
    string ServerName,
    List<SuspiciousPlayer> Players
);

/// <summary>
/// A player with a suspicious score in a round.
/// </summary>
public record SuspiciousPlayer(
    string Name,
    int Score,
    int Kills,
    int Deaths
);
