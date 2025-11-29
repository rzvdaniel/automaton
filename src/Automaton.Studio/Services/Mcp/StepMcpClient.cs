using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// MCP client implementation for Studio/Step Designer context.
/// Handles tool discovery and execution for custom step development (Python, PowerShell, Batch).
/// </summary>
public class StepMcpClient : McpClient, IStudioMcpClient
{
    public StepMcpClient(
        ILogger<StepMcpClient> logger,
        StepMcpToolProvider toolProvider)
        : base(logger, toolProvider)
    {
    }
}
