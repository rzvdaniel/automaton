using System.Text.Json;

namespace Automaton.Mcp.Client.Services;

/// <summary>
/// Represents an MCP tool that can be called by AI
/// </summary>
public class McpTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonDocument? InputSchema { get; set; }
}

/// <summary>
/// Result of tool execution
/// </summary>
public class McpToolResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Provides MCP tools for AI interactions
/// </summary>
public interface IMcpToolProvider
{
    Task<IEnumerable<McpTool>> GetToolsAsync();
    Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? currentStepId = null);
}