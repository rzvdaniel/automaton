using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Single agent mode: full read/write access to all tools.
/// The AI decides when to plan vs implement based on task complexity.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => ConversationModes.Agent;

    public string SystemPromptSuffix =>
        "\n\n## RULES\n" +
        "For simple changes (bug fixes, small edits, adding a field): implement directly.\n\n" +
        "For complex tasks (new feature, significant redesign, multiple files/unknowns): " +
        "call `propose_plan` first. Write the plan, then reply with a short plain-text message " +
        "summarising the plan and asking whether to proceed. Do not call any other tools after propose_plan " +
        "until the user confirms.\n\n" +
        "If the user asks a question or wants an explanation: answer in plain text, do not touch any files.";

    public int MaxToolIterations => 20;

    /// <summary>All tools are available in the single agent mode.</summary>
    public bool IsToolAllowed(McpTool tool) => true;
}
