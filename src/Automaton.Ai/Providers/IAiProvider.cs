using Automaton.Ai.Models;

namespace Automaton.Ai.Providers;

/// <summary>
/// The result returned by every provider after a full conversation turn.
/// <para>
/// <see cref="ApiCallCount"/> is the number of round-trips made to the LLM during the turn
/// (one per while-loop iteration). Placing it here — rather than in a streaming event — makes
/// it a compiler-enforced contract: any new provider that doesn't return a count will not build.
/// </para>
/// </summary>
public record ProviderResult(string ResponseText, int ApiCallCount);

/// <summary>
/// Interface for AI provider implementations (OpenAI, Anthropic, etc.)
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Unique provider name that matches AiProvider.Name in database (e.g., "OpenAI", "Anthropic")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// The default model used when <see cref="ConversationRequest.Model"/> is null or empty.
    /// Every provider must declare a sensible default so callers never receive a null-model error.
    /// </summary>
    string DefaultModel { get; }

    /// <summary>
    /// Event fired when conversation is updated during streaming
    /// </summary>
    event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <summary>
    /// Calls the AI provider with the given request and tools.
    /// </summary>
    /// <param name="request">The conversation request</param>
    /// <param name="tools">Available MCP tools for the AI to use</param>
    /// <param name="executeToolAsync">Function to execute tool calls</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A <see cref="ProviderResult"/> containing the response text and the number of LLM API
    /// calls made. Returning an accurate <see cref="ProviderResult.ApiCallCount"/> is required —
    /// it is stored in the database and shown in usage dashboards.
    /// </returns>
    Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents a tool that can be used by AI providers
/// </summary>
public class ProviderTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InputSchemaJson { get; set; } = "{}";
}
