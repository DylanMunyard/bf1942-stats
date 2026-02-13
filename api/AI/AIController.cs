using System.ClientModel;
using System.Text;
using System.Text.Json;
using api.AI.Models;
using api.DiscordNotifications;
using api.DiscordNotifications.Models;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace api.AI;

/// <summary>
/// Controller for AI chat functionality.
/// </summary>
[ApiController]
[Route("stats/[controller]")]
public class AIController(
    IAIService aiService,
    IDiscordWebhookService discordWebhookService,
    PlayerTrackerDbContext dbContext,
    IOptions<AzureOpenAIOptions> aiOptions,
    ILogger<AIController> logger) : ControllerBase
{
    /// <summary>
    /// Streams a chat response using Server-Sent Events.
    /// </summary>
    /// <param name="request">The chat request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("chat")]
    [EnableRateLimiting("ai-chat")]
    [Produces("text/event-stream")]
    public async Task StreamChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("AI chat request received: {MessagePreview}",
            request.Message.Length > 50 ? request.Message[..50] + "..." : request.Message);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            var fullResponse = new StringBuilder();

            await foreach (var chunk in aiService.StreamChatAsync(request, cancellationToken))
            {
                fullResponse.Append(chunk);
                var data = JsonSerializer.Serialize(new { content = chunk });
                await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Parse quality assessment from response
            var (quality, _) = AIService.ParseQualityAssessment(fullResponse.ToString());

            // Log quality assessment for analytics
            LogQualityAssessment(quality, request.Message);

            // Send quality metadata to client before [DONE]
            if (quality != null)
            {
                var qualityData = JsonSerializer.Serialize(new { quality });
                await Response.WriteAsync($"data: {qualityData}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            logger.LogDebug("AI chat stream completed successfully");
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("AI chat stream was cancelled by client");
        }
        catch (ClientResultException ex) when (ex.Message.Contains("DeploymentNotFound", StringComparison.OrdinalIgnoreCase))
        {
            var deploymentName = aiOptions.Value?.DeploymentName ?? "(not set)";
            logger.LogError(
                "Azure OpenAI deployment not found. Ensure AzureOpenAI:DeploymentName in config matches the deployment name in your Azure OpenAI resource (Azure Portal → your resource → Model deployments). Currently using deployment name: {DeploymentName}. Inner: {Message}",
                deploymentName,
                ex.Message);

            var errorData = JsonSerializer.Serialize(new
            {
                error = "AI service configuration error: the configured deployment was not found. Please ensure AzureOpenAI:DeploymentName matches the deployment name in your Azure OpenAI resource (see Azure Portal → your resource → Model deployments)."
            });
            await Response.WriteAsync($"data: {errorData}\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during AI chat stream");

            var errorData = JsonSerializer.Serialize(new { error = "An error occurred processing your request." });
            await Response.WriteAsync($"data: {errorData}\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Search for servers by name (for @ mentions).
    /// </summary>
    [HttpGet("search/servers")]
    public async Task<ActionResult<object>> SearchServers([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Ok(new { servers = Array.Empty<object>() });
        }

        var queryLower = query.ToLowerInvariant();
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Name.ToLower().Contains(queryLower))
            .OrderBy(s => s.Name)
            .Take(10)
            .Select(s => new
            {
                serverGuid = s.Guid,
                serverName = s.Name,
                gameId = s.GameId
            })
            .ToListAsync();

        return Ok(new { servers });
    }

    /// <summary>
    /// Search for players by name (for @ mentions).
    /// </summary>
    [HttpGet("search/players")]
    public async Task<ActionResult<object>> SearchPlayers([FromQuery] string query, [FromQuery] string game = "bf1942")
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Ok(new { players = Array.Empty<object>() });
        }

        // Normalize game name
        var normalizedGame = game.ToLowerInvariant() switch
        {
            "bf1942" or "battlefield 1942" => "bf1942",
            "fh2" or "forgotten hope 2" => "fh2",
            "bfvietnam" or "battlefield vietnam" => "bfvietnam",
            _ => "bf1942"
        };

        // Get server GUIDs for the specified game
        var serverGuids = await dbContext.Servers
            .AsNoTracking()
            .Where(s => s.Game == normalizedGame)
            .Select(s => s.Guid)
            .ToListAsync();

        if (serverGuids.Count == 0)
        {
            return Ok(new { players = Array.Empty<object>() });
        }

        // Search players via PlayerMapStats (aggregated stats)
        var queryLower = query.ToLowerInvariant();
        var players = await dbContext.PlayerMapStats
            .AsNoTracking()
            .Where(pms => serverGuids.Contains(pms.ServerGuid) && pms.PlayerName.ToLower().Contains(queryLower))
            .GroupBy(pms => pms.PlayerName)
            .Select(g => new
            {
                playerName = g.Key,
                totalScore = g.Sum(pms => pms.TotalScore)
            })
            .OrderByDescending(p => p.totalScore)
            .Take(10)
            .ToListAsync();

        return Ok(new { players });
    }

    /// <summary>
    /// Submit feedback (thumbs up/down) for an AI chat response.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<ActionResult> SubmitFeedback([FromBody] SubmitFeedbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Response))
        {
            return BadRequest(new { error = "Prompt and response are required." });
        }

        var feedback = new Models.AIChatFeedback
        {
            Prompt = request.Prompt.Length > 2000 ? request.Prompt[..2000] : request.Prompt,
            Response = request.Response.Length > 8000 ? request.Response[..8000] : request.Response,
            IsPositive = request.IsPositive,
            Comment = request.Comment?.Length > 500 ? request.Comment[..500] : request.Comment,
            PageContext = request.PageContext,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AIChatFeedback.Add(feedback);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI chat feedback received: {Rating}, prompt: {Prompt}",
            request.IsPositive ? "positive" : "negative",
            request.Prompt.Length > 80 ? request.Prompt[..80] + "..." : request.Prompt);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Gets AI chat feedback entries (admin only).
    /// </summary>
    [HttpGet("feedback")]
    public async Task<ActionResult<object>> GetFeedback(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? isPositive = null)
    {
        var query = dbContext.AIChatFeedback.AsNoTracking();

        if (isPositive.HasValue)
        {
            query = query.Where(f => f.IsPositive == isPositive.Value);
        }

        var totalCount = await query.CountAsync();
        var positiveCount = await dbContext.AIChatFeedback.AsNoTracking().CountAsync(f => f.IsPositive);
        var negativeCount = await dbContext.AIChatFeedback.AsNoTracking().CountAsync(f => !f.IsPositive);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.Prompt,
                f.Response,
                f.IsPositive,
                f.Comment,
                f.PageContext,
                f.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            items,
            totalCount,
            positiveCount,
            negativeCount,
            page,
            pageSize
        });
    }

    /// <summary>
    /// Health check endpoint for AI service.
    /// </summary>
    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        return Ok(new { status = "ok", service = "ai-chat" });
    }

    /// <summary>
    /// Logs quality assessment metrics for analytics and kernel method improvement.
    /// </summary>
    private void LogQualityAssessment(QualityAssessment? quality, string userMessage)
    {
        using var activity = ActivitySources.AIChat.StartActivity("QualityAssessment");

        if (quality == null)
        {
            logger.LogWarning("AI response did not include quality assessment marker");
            activity?.SetTag("ai.quality.parsed", false);
            return;
        }

        activity?.SetTag("ai.quality.parsed", true);
        activity?.SetTag("ai.quality.confidence", quality.Confidence);
        activity?.SetTag("ai.quality.sufficient_kernel_methods", quality.SufficientKernelMethods);

        if (quality.MissingContext.Length > 0)
        {
            activity?.SetTag("ai.quality.missing_context", string.Join(", ", quality.MissingContext));
        }

        if (quality.SuggestedKernelMethods.Length > 0)
        {
            activity?.SetTag("ai.quality.suggested_methods", string.Join(", ", quality.SuggestedKernelMethods));
        }

        // Log for easy querying - especially interested in low confidence or missing kernel methods
        if (quality.Confidence == "low" || !quality.SufficientKernelMethods)
        {
            logger.LogInformation(
                "AI quality gap detected. Confidence: {Confidence}, SufficientMethods: {Sufficient}, " +
                "MissingContext: [{MissingContext}], SuggestedMethods: [{SuggestedMethods}], UserMessage: {Message}",
                quality.Confidence,
                quality.SufficientKernelMethods,
                string.Join(", ", quality.MissingContext),
                string.Join(", ", quality.SuggestedKernelMethods),
                userMessage.Length > 200 ? userMessage[..200] + "..." : userMessage);

            // Send Discord alert for low quality responses (fire-and-forget)
            _ = discordWebhookService.SendAIQualityAlertAsync(new AIQualityAlert(
                quality.Confidence,
                quality.SufficientKernelMethods,
                quality.MissingContext,
                quality.SuggestedKernelMethods,
                userMessage));
        }
        else
        {
            logger.LogDebug(
                "AI quality assessment: Confidence={Confidence}, SufficientMethods={Sufficient}",
                quality.Confidence,
                quality.SufficientKernelMethods);
        }
    }
}
