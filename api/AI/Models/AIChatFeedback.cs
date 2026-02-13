namespace api.AI.Models;

/// <summary>
/// Stores user feedback (thumbs up/down) for AI chat responses.
/// </summary>
public class AIChatFeedback
{
    public int Id { get; set; }

    /// <summary>The user's original prompt/message.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>The AI's response (cleaned, without quality marker).</summary>
    public string Response { get; set; } = "";

    /// <summary>Rating: true = thumbs up, false = thumbs down.</summary>
    public bool IsPositive { get; set; }

    /// <summary>Optional comment from the user explaining the rating.</summary>
    public string? Comment { get; set; }

    /// <summary>Page context at the time of the conversation (JSON).</summary>
    public string? PageContext { get; set; }

    /// <summary>When the feedback was submitted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
