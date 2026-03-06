using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: full read/write access. No planning tools.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => "agent";

    public string SystemPromptSuffix =>
        "\n\n## CURRENT MODE: Agent\n" +
        "You are in **agent mode**. Implement tasks directly using the available tools.\n" +
        "Planning tools are not available in this mode.";

    /// <summary>Allow Read and Write tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Planning;
}
