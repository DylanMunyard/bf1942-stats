namespace api.AI.Models;

/// <summary>
/// Represents a message in the conversation history.
/// </summary>
public record ChatMessage
{
    /// <summary>
    /// Role of the message sender: "user" or "assistant".
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Content of the message.
    /// </summary>
    public required string Content { get; init; }
}
