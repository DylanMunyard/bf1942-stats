namespace api.AI.Models;

/// <summary>
/// Self-assessment of answer quality from the AI.
/// Used to identify questions that need new kernel methods.
/// </summary>
public record QualityAssessment
{
    /// <summary>
    /// Confidence level in the answer quality.
    /// </summary>
    public required string Confidence { get; init; }

    /// <summary>
    /// What context or data was missing that would have improved the answer.
    /// Empty if the answer was fully satisfactory.
    /// </summary>
    public string[] MissingContext { get; init; } = [];

    /// <summary>
    /// Whether the available kernel methods (plugins) were sufficient.
    /// </summary>
    public bool SufficientKernelMethods { get; init; } = true;

    /// <summary>
    /// Suggested kernel methods that would help answer similar questions.
    /// </summary>
    public string[] SuggestedKernelMethods { get; init; } = [];
}
