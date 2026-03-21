using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: full read/write access, no planning tools.
/// Users switch to plan mode manually via the UI when they want to plan first.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => ConversationModes.Agent;

    public string SystemPromptSuffix =>
        "\n\n## RULES\n" +
        "Proceed directly for all changes — bug fixes, small edits, and multi-file features alike.\n\n" +
        "If the user asks a question or wants an explanation, answer directly without touching any files.\n\n" +
        "If the task is complex enough to benefit from a plan first (new feature, significant redesign, " +
        "multiple unknowns), call `propose_plan` before writing any files. " +
        "After propose_plan, STOP and ask: 'Plan is ready — shall I implement it?' " +
        "Do NOT call propose_plan for simple or clear changes — just implement.";

    public int MaxToolIterations => 20;

    /// <summary>All tools are available in the single agent mode.</summary>
    public bool IsToolAllowed(McpTool tool) => true;
}
