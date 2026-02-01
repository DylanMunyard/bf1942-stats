using api.DiscordNotifications.Models;

namespace api.DiscordNotifications;

public interface IDiscordWebhookService
{
    /// <summary>
    /// Score threshold used to determine suspicious rounds. From DiscordSuspicious:ScoreThreshold (default 200).
    /// </summary>
    int ScoreThreshold { get; }

    /// <summary>
    /// Sends a Discord notification for a round with suspicious player scores.
    /// </summary>
    Task SendSuspiciousRoundAlertAsync(SuspiciousRoundAlert alert);

    /// <summary>
    /// Sends a Discord notification for a low-quality AI response.
    /// </summary>
    Task SendAIQualityAlertAsync(AIQualityAlert alert);
}
