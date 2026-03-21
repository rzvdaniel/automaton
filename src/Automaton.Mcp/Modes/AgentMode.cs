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
        "\n\n## WHEN TO SUGGEST PLAN MODE\n" +
        "Proceed directly for all changes — bug fixes, small edits, and multi-file features alike.\n\n" +
        "If the user asks a question or wants an explanation, answer directly without touching any files.\n\n" +
        "If the task is large enough that the user would benefit from reviewing an approach first " +
        "(e.g. a significant new feature, a database addition, a full redesign), call `switch_mode(to: \"plan\")` " +
        "then explain what you want to discuss before implementing. Do NOT call switch_mode for simple changes.";

    public int MaxToolIterations => 20;

    /// <summary>Allow Read and Write tools; block Planning tools (plan mode only).</summary>
    public bool IsToolAllowed(McpTool tool) =>
        tool.Capability != ToolCapability.Planning;
}
