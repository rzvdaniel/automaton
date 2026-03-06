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

    /// <summary>
    /// Declares what category this tool belongs to.
    /// Conversation modes filter the tool list based on this value — no hardcoded name lists needed.
    /// </summary>
    public ToolCapability Capability { get; set; } = ToolCapability.Read;
}
