namespace Automaton.Mcp.Models;

/// <summary>
/// Represents a conversation message in the MCP client
/// </summary>
public record ConversationMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Indicates if this message is still being streamed (partial content)
    /// </summary>
    public bool IsStreaming { get; init; } = false;
    
    /// <summary>
    /// Token usage for AI-generated messages
    /// </summary>
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string? Model { get; init; }

    /// <summary>
    /// Anthropic prompt cache stats (null for non-Anthropic providers or when cache is not involved)
    /// </summary>
    public int? CacheReadTokens { get; init; }
    public int? CacheCreationTokens { get; init; }

    /// <summary>
    /// Internal reasoning / chain-of-thought produced by thinking models (e.g. Moonshot kimi-k2.5).
    /// Null for models that do not emit reasoning_content.
    /// </summary>
    public string? ReasoningContent { get; init; }
}
