using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: unrestricted access to all tools.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => "agent";

    public string SystemPromptSuffix => string.Empty;

    /// <summary>All tools are allowed in agent mode.</summary>
    public bool IsToolAllowed(McpTool tool) => true;
}
