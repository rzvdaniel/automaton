using Automaton.Mcp.Models;

namespace Automaton.Mcp.Client.Providers;

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
    /// Event fired when conversation is updated during streaming
    /// </summary>
    event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <summary>
    /// Calls the AI provider with the given request and tools
    /// </summary>
    /// <param name="request">The conversation request</param>
    /// <param name="tools">Available MCP tools for the AI to use</param>
    /// <param name="executeToolAsync">Function to execute tool calls</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The response text from the AI</returns>
    Task<string> CallAsync(
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
