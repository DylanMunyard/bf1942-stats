namespace api.DiscordNotifications;

/// <summary>
/// Configuration for Discord AI quality alerts.
/// Bound from config section "DiscordAIQuality" (env: DiscordAIQuality__WebhookUrl).
/// </summary>
public class DiscordAIQualityOptions
{
    /// <summary>
    /// Discord webhook URL for AI quality alerts. When null or empty, alerts are skipped.
    /// Typically provided from Kubernetes secret (e.g. discord-secrets.ai-quality-webhook).
    /// </summary>
    public string? WebhookUrl { get; set; }
}
