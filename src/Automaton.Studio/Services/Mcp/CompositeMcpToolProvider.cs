using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Composite MCP tool provider that delegates to the appropriate provider based on context.
/// This enables clear separation of concerns between Step and WebApp MCP tools.
/// </summary>
public class CompositeMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<CompositeMcpToolProvider> _logger;
    private readonly StudioMcpToolProvider _stepToolProvider;
    private readonly WebAppMcpToolProvider _webAppToolProvider;

    public CompositeMcpToolProvider(
        ILogger<CompositeMcpToolProvider> logger,
        StudioMcpToolProvider stepToolProvider,
        WebAppMcpToolProvider webAppToolProvider)
    {
        _logger = logger;
        _stepToolProvider = stepToolProvider;
        _webAppToolProvider = webAppToolProvider;
    }

    public async Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        // Return all tools from both providers
        // The context will determine which tools are actually used
        var stepTools = await _stepToolProvider.GetToolsAsync();
        var webAppTools = await _webAppToolProvider.GetToolsAsync();

        // WebApp tools have simpler schema - they'll be used based on context
        return stepTools.Concat(webAppTools).DistinctBy(t => t.Name);
    }

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? contextId = null, Dictionary<string, object>? context = null)
    {
        // Determine which provider to use based on context
        var isWebAppContext = IsWebAppContext(context);

        _logger.LogDebug("Executing tool {ToolName} with {ContextType} context", 
            toolName, isWebAppContext ? "WebApp" : "Step");

        if (isWebAppContext)
        {
            return await _webAppToolProvider.ExecuteToolAsync(toolName, arguments, contextId, context);
        }
        else
        {
            return await _stepToolProvider.ExecuteToolAsync(toolName, arguments, contextId, context);
        }
    }

    /// <summary>
    /// Determine if the context is for a WebApp based on the language or other indicators
    /// </summary>
    private bool IsWebAppContext(Dictionary<string, object>? context)
    {
        if (context == null) return false;

        // Check for HTML language indicator
        if (context.TryGetValue("language", out var langObj))
        {
            var language = langObj?.ToString()?.ToLowerInvariant();
            if (language == "html" || language == "css" || language == "javascript")
            {
                return true;
            }
        }

        // Check for WebApp-specific context markers
        if (context.ContainsKey("isWebApp") && context["isWebApp"] is bool isWebApp && isWebApp)
        {
            return true;
        }

        return false;
    }
}
