namespace api.AI.Models;

/// <summary>
/// Configuration options for Azure OpenAI integration.
/// </summary>
public record AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Azure OpenAI endpoint URL.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// Azure OpenAI API key.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Deployment name. Must match exactly the deployment name in your Azure OpenAI resource
    /// (Azure Portal → your resource → Model deployments). Example: "gpt-4o-mini" if that is the name you gave the deployment.
    /// </summary>
    public required string DeploymentName { get; init; }

    /// <summary>
    /// Maximum tokens for response generation.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0 to 1.0).
    /// </summary>
    public double Temperature { get; init; } = 0.7;
}
