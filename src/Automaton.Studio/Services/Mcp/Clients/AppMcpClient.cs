using Automaton.Studio.Services.Mcp.ToolProviders;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client implementation for WebApp Designer context.
/// Handles tool discovery and execution for web app development (HTML, CSS, JavaScript).
/// </summary>
public class AppMcpClient : McpClient, IAppMcpClient
{
    public AppMcpClient(
        ILogger<AppMcpClient> logger,
        AppMcpToolProvider toolProvider,
        IAiProviderRegistry providerRegistry)
        : base(logger, toolProvider, providerRegistry)
    {
    }
}
