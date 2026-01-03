using Automaton.Studio.Services.Mcp.ToolProviders;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client implementation for App Designer context.
/// Handles tool discovery and execution for both web app development (HTML, CSS, JavaScript)
/// and backend Custom Step creation (Python, Batch).
/// </summary>
public class AppMcpClient : McpClient, IAppMcpClient
{
    private readonly AppMcpToolProvider _toolProvider;

    /// <summary>
    /// Event fired when backend steps are created, updated, or linked to the app.
    /// Proxies the event from the underlying tool provider.
    /// </summary>
    public event EventHandler<BackendStepsChangedEventArgs>? BackendStepsChanged
    {
        add => _toolProvider.BackendStepsChanged += value;
        remove => _toolProvider.BackendStepsChanged -= value;
    }

    public AppMcpClient(
        ILogger<AppMcpClient> logger,
        AppMcpToolProvider toolProvider,
        IAiProviderRegistry providerRegistry)
        : base(logger, toolProvider, providerRegistry)
    {
        _toolProvider = toolProvider;
    }

    /// <summary>
    /// Set the current app context for this MCP client session.
    /// This allows the tool provider to track which app backend steps should be linked to.
    /// </summary>
    public void SetCurrentApp(Guid appId)
    {
        _toolProvider.SetCurrentApp(appId);
    }
}
