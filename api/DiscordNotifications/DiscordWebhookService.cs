using System.Net.Http.Json;
using System.Text;
using api.DiscordNotifications.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace api.DiscordNotifications;

public class DiscordWebhookService(
    IHttpClientFactory httpClientFactory,
    IOptions<DiscordSuspiciousOptions> options,
    ILogger<DiscordWebhookService> logger) : IDiscordWebhookService
{
    private readonly DiscordSuspiciousOptions _options = options.Value;

    public int ScoreThreshold => _options.ScoreThreshold;

    public async Task SendSuspiciousRoundAlertAsync(SuspiciousRoundAlert alert)
    {
        if (string.IsNullOrEmpty(_options.RoundWebhookUrl))
        {
            logger.LogDebug("Discord webhook URL not configured, skipping suspicious round alert");
            return;
        }

        try
        {
            var embed = BuildEmbed(alert);
            var payload = new { embeds = new[] { embed } };

            var client = httpClientFactory.CreateClient("DiscordWebhook");
            var response = await client.PostAsJsonAsync(_options.RoundWebhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning(
                    "Discord webhook returned {StatusCode}: {Body}",
                    response.StatusCode, body);
            }
            else
            {
                logger.LogInformation(
                    "Sent suspicious round alert for round {RoundId} with {PlayerCount} players",
                    alert.RoundId, alert.Players.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Discord suspicious round alert for round {RoundId}", alert.RoundId);
        }
    }

    private object BuildEmbed(SuspiciousRoundAlert alert)
    {
        var playerLines = alert.Players
            .OrderByDescending(p => p.Score)
            .Select(p => $"\u2022 **{p.Name}**: {p.Score} score ({p.Kills} kills, {p.Deaths} deaths)");

        var roundUrl = $"https://bfstats.io/rounds/{alert.RoundId}/report";

        var description = new StringBuilder();
        description.AppendLine($"**{alert.MapName}** on **{alert.ServerName}**");
        description.AppendLine($"Player scores >= {_options.ScoreThreshold}");
        description.AppendLine();
        description.AppendLine("**Players:**");
        foreach (var line in playerLines)
        {
            description.AppendLine(line);
        }
        return new
        {
            title = "\ud83d\udea8 Suspicious Round Detected",
            description = description.ToString(),
            color = 15158332, // Red color
            url = roundUrl,
            timestamp = DateTime.UtcNow.ToString("o"),
            author = new
            {
                name = "🔗 View Round Report",
                url = roundUrl
            }
        };
    }
}
