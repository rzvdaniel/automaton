using System.Text.Json;

namespace Automaton.Mcp.Models;

/// <summary>
/// Represents an MCP tool that can be called by AI
/// </summary>
public class McpTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonDocument? InputSchema { get; set; }
}
