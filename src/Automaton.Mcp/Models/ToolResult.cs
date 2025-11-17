namespace Automaton.Mcp.Models;

/// <summary>
/// Result of a tool execution
/// </summary>
public class ToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
