using System.Text.Json;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Server.Services.Mcp.Tools;

/// <summary>
/// A cohesive group of related MCP tools (e.g. file IO, flow operations).
/// Register implementations with DI — the orchestrator picks them up automatically.
/// </summary>
public interface IMcpTool
{
    /// <summary>Returns the tool definitions exposed by this group.</summary>
    Task<IEnumerable<McpTool>> GetToolsAsync();

    /// <summary>Returns true if this group owns the given tool name.</summary>
    bool CanHandle(string toolName);

    /// <summary>Executes a tool by name with the given arguments and conversation context.</summary>
    Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context);
}
