using Automaton.Studio.Services.Mcp.ToolProviders;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client implementation for unified App Designer context.
/// Handles tool discovery and execution for both web app development (HTML, CSS, JavaScript)
/// and backend Custom Step creation (Python, Batch).
/// </summary>
public class UnifiedAppMcpClient : McpClient, IUnifiedAppMcpClient
{
    private readonly UnifiedAppMcpToolProvider _unifiedToolProvider;

    public UnifiedAppMcpClient(
        ILogger<UnifiedAppMcpClient> logger,
        UnifiedAppMcpToolProvider toolProvider)
        : base(logger, toolProvider)
    {
        _unifiedToolProvider = toolProvider;
    }

    /// <summary>
    /// Set the current app context for this MCP client session.
    /// This allows the tool provider to track which app backend steps should be linked to.
    /// </summary>
    public void SetCurrentApp(Guid appId)
    {
        _unifiedToolProvider.SetCurrentApp(appId);
    }
}
