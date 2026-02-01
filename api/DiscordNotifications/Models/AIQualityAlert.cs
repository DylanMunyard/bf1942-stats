namespace api.DiscordNotifications.Models;

/// <summary>
/// Alert data for a low-quality AI response.
/// </summary>
public record AIQualityAlert(
    string Confidence,
    bool SufficientKernelMethods,
    string[] MissingContext,
    string[] SuggestedKernelMethods,
    string UserMessage
);
