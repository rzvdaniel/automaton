using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Defines the tool filtering and system prompt rules for a conversation mode.
/// Implement this interface to add new modes — no changes needed in AutomatonMcpClient
/// or AiConversationControllerBase.
/// </summary>
public interface IConversationMode
{
    /// <summary>Stable identifier (e.g. "agent", "plan").</summary>
    string Name { get; }

    /// <summary>
    /// Appended to the system prompt so the model knows what tools are available in this mode.
    /// </summary>
    string SystemPromptSuffix { get; }

    /// <summary>Returns true if the tool is allowed in this mode.</summary>
    bool IsToolAllowed(McpTool tool);
}
