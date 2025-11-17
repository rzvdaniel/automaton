namespace Automaton.Mcp.Models;

/// <summary>
/// Represents a tool call made by the AI
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}
