using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using api.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

namespace api.AI;

/// <summary>
/// AI chat service using Semantic Kernel with Azure OpenAI.
/// </summary>
public partial class AIService(
    Kernel kernel,
    IOptions<AzureOpenAIOptions> options,
    ILogger<AIService> logger) : IAIService
{
    [GeneratedRegex(@"\[BFSTATS_QUALITY:\s*(\{.*?\})\s*\]", RegexOptions.Singleline)]
    private static partial Regex QualityMarkerRegex();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt = """
        You are BFStats AI, an assistant for the BFStats.io Battlefield 1942 statistics website.
        You help users understand player statistics, find game activity patterns, and explore server data.

        Available data includes:
        - Player lifetime stats (kills, deaths, score, K/D ratio, playtime)
        - Player performance by server and map
        - Player best scores
        - Server leaderboards and activity
        - GLOBAL leaderboards: Use GetTopPlayersByKDRatio and GetTopPlayersByKillRate for questions about top players by K/D or kill rate across all servers (supports minimum rounds filter)
        - Round history and game type analysis
        - Activity patterns (when games happen)
        - CURRENT/LIVE server data: Use GetTopServersByCurrentPlayers and GetMapsOnServersWithMinPlayers for questions about servers playing "right now" or "currently active"

        Format your responses in Markdown so they render correctly in the UI. Prioritise readability so users can scan quickly.
        - **Tables** for stats, rankings, leaderboards, and any data with multiple columns (e.g. Player | Score | K/D). Use Markdown tables: | Col1 | Col2 | and --- for header row. Tables are easier to scan than long bullet lists.
        - ### for section headings (e.g. "Top Maps Played", "Leaderboard")
        - **bold** for emphasis (e.g. server names, key numbers)
        - Bullet or numbered lists only when a table would not fit (e.g. a short list of tips or a single-column list)

        Guidelines:
        - Be concise but informative. Consider readability: structure responses so they are quick to scan (tables over long lists where it makes sense).
        - When showing statistics, highlight interesting patterns or notable achievements
        - If data is unavailable, suggest alternatives or explain what might help
        - Use the page context when available to provide relevant information
        - Format numbers nicely (e.g., "1,234 kills" not "1234 kills")
        - For K/D ratios, 2 decimal places is sufficient
        - Convert playtime to hours when appropriate (e.g., "45.2 hours" not "2712 minutes")
        - When discussing times, note that they are in UTC
        - For questions about "currently active servers", "servers playing right now", or "what maps are being played", use GetTopServersByCurrentPlayers or GetMapsOnServersWithMinPlayers. These return real-time current data, not historical averages.
        - For questions about "top players by K/D", "best kill rate", "highest kills per minute", or any leaderboard question with a minimum rounds/games requirement, use GetTopPlayersByKDRatio or GetTopPlayersByKillRate. These support minRounds filters (e.g., "minimum 20 games") and work across all servers or for a specific server. Do NOT use SearchPlayers for leaderboard questions — SearchPlayers is only for finding a player by name.
        - Keep responses concise: focus on directly answering the question without unnecessary elaboration

        Naming convention for the UI (important):
        - When you mention a player by name (from context or from query results), wrap the exact display name in «player:name» so it can be shown as a player badge. Example: «player:SomePlayer» or «player:Player With Spaces».
        - When you mention a server by name, wrap the exact display name in «server:name» so it can be shown as a server badge. Example: «server:MoonGamers.com | Est. 2004». The name must be the full display name including any spaces, punctuation, or symbols; do not put » inside the name.
        - Use these delimiters so the UI can reliably identify and style player and server names even when they contain spaces, pipes, or other characters.

        CRITICAL TABLE FORMATTING RULE:
        - Many server and player names contain pipe characters (|), asterisks (*), and other Markdown-special characters (e.g. "MoonGamers.com | Est. 2004", "*NEW* SiMPLE | BF1942").
        - When placing ANY name inside a Markdown table cell, you MUST escape pipe characters as \| and asterisks as \*. Otherwise the pipe will be interpreted as a column delimiter, breaking the table layout.
        - Example: | MoonGamers.com \| Est. 2004 | 11 | wake |
        - This applies to ALL values inside table cells, not just names. Always escape | and * in table cell content.

        You have access to functions that can query the database. Use them to get real data.
        If the user asks about "this player" or "this server", use the context provided.

        QUALITY ASSESSMENT (required at end of every response):
        After your response, on a new line, append a quality assessment in this exact format:
        [BFSTATS_QUALITY:{"confidence":"high|medium|low","missingContext":[],"sufficientKernelMethods":true,"suggestedKernelMethods":[]}]

        Guidelines for quality assessment:
        - confidence: "high" if you fully answered the question with available data, "medium" if partially answered, "low" if you couldn't provide a good answer
        - missingContext: Array of strings describing what data/context would have helped (e.g., ["player's clan history", "historical server population data"])
        - sufficientKernelMethods: false if you needed data that no available function could provide
        - suggestedKernelMethods: Array of function names that would help (e.g., ["GetPlayerClanHistory", "GetServerPopulationHistory"])

        This assessment helps us improve the system. Always include it, even for simple questions.
        """;

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Starting AI chat stream for message: {Message}", request.Message);

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(BuildSystemPromptWithContext(request.Context));

        // Add conversation history
        if (request.ConversationHistory != null)
        {
            foreach (var message in request.ConversationHistory)
            {
                if (message.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    chatHistory.AddUserMessage(message.Content);
                }
                else if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    chatHistory.AddAssistantMessage(message.Content);
                }
            }
        }

        // Add current message
        chatHistory.AddUserMessage(request.Message);

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        var executionSettings = new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = options.Value.MaxTokens,
            Temperature = options.Value.Temperature,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var fullResponse = new StringBuilder();

        await foreach (var chunk in chatCompletionService.GetStreamingChatMessageContentsAsync(
            chatHistory,
            executionSettings,
            kernel,
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                fullResponse.Append(chunk.Content);
                yield return chunk.Content;
            }
        }

        logger.LogDebug("AI chat stream completed. Response length: {Length}", fullResponse.Length);
    }

    private static string BuildSystemPromptWithContext(PageContext? context)
    {
        if (context == null)
        {
            return SystemPrompt;
        }

        var contextInfo = new StringBuilder();
        contextInfo.AppendLine(SystemPrompt);
        contextInfo.AppendLine();
        contextInfo.AppendLine("Current page context:");

        if (!string.IsNullOrEmpty(context.PageType))
        {
            contextInfo.AppendLine($"- Page type: {context.PageType}");
        }

        if (!string.IsNullOrEmpty(context.PlayerName))
        {
            contextInfo.AppendLine($"- Current player: {context.PlayerName}");
            contextInfo.AppendLine("When the user says 'this player' or 'my stats', they mean this player.");
        }

        if (!string.IsNullOrEmpty(context.ServerGuid))
        {
            contextInfo.AppendLine($"- Current server GUID: {context.ServerGuid}");
            contextInfo.AppendLine("When the user says 'this server', they mean this server.");
        }

        if (!string.IsNullOrEmpty(context.ServerName))
        {
            contextInfo.AppendLine($"- Current server name (use this exact name when calling server-related functions): {context.ServerName}");
        }

        if (!string.IsNullOrEmpty(context.Game))
        {
            contextInfo.AppendLine($"- Game: {context.Game}");
        }

        return contextInfo.ToString();
    }

    /// <summary>
    /// Parses the quality assessment marker from a complete AI response.
    /// </summary>
    /// <param name="fullResponse">The complete response text.</param>
    /// <returns>The parsed quality assessment, or null if not found or invalid.</returns>
    public static (QualityAssessment? Assessment, string CleanedResponse) ParseQualityAssessment(string fullResponse)
    {
        var match = QualityMarkerRegex().Match(fullResponse);
        if (!match.Success)
        {
            return (null, fullResponse);
        }

        var jsonPart = match.Groups[1].Value;
        var cleanedResponse = fullResponse[..match.Index].TrimEnd();

        try
        {
            var assessment = JsonSerializer.Deserialize<QualityAssessment>(jsonPart, JsonOptions);
            return (assessment, cleanedResponse);
        }
        catch (JsonException)
        {
            return (null, cleanedResponse);
        }
    }
}
