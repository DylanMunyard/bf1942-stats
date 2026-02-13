namespace api.AI.Models;

/// <summary>
/// Request model for submitting AI chat feedback.
/// </summary>
public class SubmitFeedbackRequest
{
    /// <summary>The user's original prompt/message.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>The AI's response.</summary>
    public string Response { get; set; } = "";

    /// <summary>True = thumbs up, false = thumbs down.</summary>
    public bool IsPositive { get; set; }

    /// <summary>Optional comment from the user.</summary>
    public string? Comment { get; set; }

    /// <summary>Serialized page context (JSON).</summary>
    public string? PageContext { get; set; }
}
