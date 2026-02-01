using api.AI.Models;

namespace api.AI;

/// <summary>
/// Interface for AI chat service using Semantic Kernel.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Streams a chat response for the given request.
    /// </summary>
    /// <param name="request">The chat request containing message, context, and history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of response chunks.</returns>
    IAsyncEnumerable<string> StreamChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
