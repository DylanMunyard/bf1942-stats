namespace api.DiscordNotifications;

/// <summary>
/// Configuration for Discord suspicious round alerts.
/// Bound from config section "DiscordSuspicious" (env: DiscordSuspicious__RoundWebhookUrl, DiscordSuspicious__ScoreThreshold).
/// </summary>
public class DiscordSuspiciousOptions
{
    /// <summary>
    /// Discord webhook URL for suspicious round alerts. When null or empty, alerts are skipped.
    /// Typically provided from Kubernetes secret (e.g. discord-secrets.suspicious-round-webhook).
    /// </summary>
    public string? RoundWebhookUrl { get; set; }

    /// <summary>
    /// Score threshold to trigger an alert. Default 200.
    /// </summary>
    public int ScoreThreshold { get; set; } = 200;
}
