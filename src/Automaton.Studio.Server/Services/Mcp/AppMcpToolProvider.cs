using System.Text.Json;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Mcp.Tools;

namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Orchestrator that aggregates all registered <see cref="IMcpTool"/> groups
/// and dispatches tool calls to the correct one.
/// To add new capabilities, implement <see cref="IMcpTool"/> and register it in DI —
/// no changes needed here.
/// </summary>
public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<AppMcpToolProvider> _logger;
    private readonly IEnumerable<IMcpTool> _tools;

    public AppMcpToolProvider(ILogger<AppMcpToolProvider> logger, IEnumerable<IMcpTool> tools)
    {
        _logger = logger;
        _tools  = tools;
    }

    public async Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var all = new List<McpTool>();
        foreach (var tool in _tools)
            all.AddRange(await tool.GetToolsAsync());
        return all;
    }

    public async Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        string? contextId = null,
        Dictionary<string, object>? context = null)
    {
        var handler = _tools.FirstOrDefault(t => t.CanHandle(toolName));

        if (handler is null)
        {
            _logger.LogWarning("No tool handler found for '{ToolName}'", toolName);
            return new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" };
        }

        try
        {
            return await handler.ExecuteToolAsync(toolName, arguments, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool '{ToolName}'", toolName);
            return new McpToolResult { Success = false, Error = $"Tool execution failed: {ex.Message}" };
        }
    }
}
