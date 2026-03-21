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
        "If the task would genuinely benefit from a plan first, say so in plain text and suggest the user " +
        "click the **Plan** button in the toolbar. Do NOT call any tool for this.";

    public int MaxToolIterations => 20;

    /// <summary>Allow Read and Write tools; block Planning and ModeSwitch tools (plan mode only).</summary>
    public bool IsToolAllowed(McpTool tool) =>
        tool.Capability != ToolCapability.Planning &&
        tool.Capability != ToolCapability.ModeSwitch;
}
