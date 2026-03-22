namespace Automaton.Mcp.Models;

/// <summary>
/// Event fired when conversation is updated
/// </summary>
public class ConversationUpdatedEvent
{
    public string ConversationId { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public List<ToolCall> ToolCalls { get; set; } = new();
    public List<ToolResult> ToolResults { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of LLM API calls (loop iterations) made during this conversation turn.
    /// Only populated in the final event emitted at the end of a provider's CallAsync.
    /// </summary>
    public int ApiCallCount { get; set; }
}
