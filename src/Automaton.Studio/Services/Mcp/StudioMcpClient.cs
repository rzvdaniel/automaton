using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// MCP client implementation for Studio/Step Designer context.
/// Handles tool discovery and execution for custom step development (Python, PowerShell, Batch).
/// </summary>
public class StudioMcpClient : BaseMcpClient, IStudioMcpClient
{
    public StudioMcpClient(
        ILogger<StudioMcpClient> logger,
        StudioMcpToolProvider toolProvider)
        : base(logger, toolProvider)
    {
    }
}
