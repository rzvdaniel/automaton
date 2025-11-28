using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Studio.Components.AiChat.Contexts;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Composite MCP tool provider that delegates to the appropriate provider based on context.
/// The context explicitly specifies which tool provider to use via the "toolProviderType" key.
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
        // Determine which provider to use based on explicit toolProviderType in context
        var providerType = GetToolProviderType(context);

        _logger.LogDebug("Executing tool {ToolName} with {ContextType} provider", 
            toolName, providerType);

        if (providerType == McpToolProviderType.WebApp)
        {
            return await _webAppToolProvider.ExecuteToolAsync(toolName, arguments, contextId, context);
        }
        else
        {
            // Default to Step provider
            return await _stepToolProvider.ExecuteToolAsync(toolName, arguments, contextId, context);
        }
    }

    /// <summary>
    /// Get the tool provider type from context. 
    /// The context explicitly specifies which provider to use via the "toolProviderType" key.
    /// </summary>
    private McpToolProviderType GetToolProviderType(Dictionary<string, object>? context)
    {
        if (context == null) return McpToolProviderType.Step;

        // Check for explicit toolProviderType marker (set by IAiChatContext.GetAdditionalContext)
        if (context.TryGetValue("toolProviderType", out var typeObj))
        {
            var typeString = typeObj?.ToString();
            if (Enum.TryParse<McpToolProviderType>(typeString, out var providerType))
            {
                return providerType;
            }
        }

        // Default to Step provider
        return McpToolProviderType.Step;
    }
}
