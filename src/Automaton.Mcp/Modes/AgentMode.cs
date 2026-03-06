using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: full read/write access, no planning tools.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => "agent";

    public string SystemPromptSuffix => string.Empty;

    public int MaxToolIterations => 20;

    /// <summary>Allow Read and Write tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Planning;
}
