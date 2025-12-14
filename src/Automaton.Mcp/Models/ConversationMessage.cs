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
}
