namespace Automaton.Mcp.Models;

/// <summary>
/// Result of tool execution
/// </summary>
public class McpToolResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? Error { get; set; }
}
