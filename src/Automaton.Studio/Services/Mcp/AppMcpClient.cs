using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// MCP client implementation for WebApp Designer context.
/// Handles tool discovery and execution for web app development (HTML, CSS, JavaScript).
/// </summary>
public class AppMcpClient : BaseMcpClient, IAppMcpClient
{
    public AppMcpClient(
        ILogger<AppMcpClient> logger,
        AppMcpToolProvider toolProvider)
        : base(logger, toolProvider)
    {
    }
}
