using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Legacy agent mode — delegates to <see cref="HumanInTheMiddleMode"/> for backward compatibility.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    private static readonly HumanInTheMiddleMode _inner = new();

    public string Name => ConversationModes.Agent;
    public string SystemPromptSuffix => _inner.SystemPromptSuffix;
    public int MaxToolIterations => _inner.MaxToolIterations;
    public bool IsToolAllowed(McpTool tool) => _inner.IsToolAllowed(tool);
}
