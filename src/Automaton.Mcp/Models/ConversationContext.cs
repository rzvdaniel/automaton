namespace Automaton.Mcp.Models;

/// <summary>
/// Context for tracking conversations
/// </summary>
public class ConversationContext
{
    public string ConversationId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}
