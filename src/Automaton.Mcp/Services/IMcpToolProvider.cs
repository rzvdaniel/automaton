using Automaton.Mcp.Models;
using System.Text.Json;

namespace Automaton.Mcp.Services;

/// <summary>
/// Provides MCP tools for AI interactions
/// </summary>
public interface IMcpToolProvider
{
    Task<IEnumerable<McpTool>> GetToolsAsync();
    Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? contextId = null, Dictionary<string, object>? context = null);
}