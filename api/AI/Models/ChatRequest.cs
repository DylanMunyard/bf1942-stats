namespace api.AI.Models;

/// <summary>
/// Request model for AI chat endpoint.
/// </summary>
public record ChatRequest
{
    /// <summary>
    /// The user's message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Current page context (optional).
    /// </summary>
    public PageContext? Context { get; init; }

    /// <summary>
    /// Previous conversation history (optional).
    /// </summary>
    public List<ChatMessage>? ConversationHistory { get; init; }
}
