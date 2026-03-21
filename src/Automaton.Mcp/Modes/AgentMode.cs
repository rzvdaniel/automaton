using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: full read/write access, no planning tools.
/// Has request_plan to escalate to plan mode for complex tasks.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => ConversationModes.Agent;

    public string SystemPromptSuffix =>
        "\n\n## STEP 1 — ASSESS ONCE PER CHANGE REQUEST\n" +
        "If the user is asking a **question or wants an explanation** — answer directly, do NOT call `assess_task`.\n\n" +
        "If the user is asking you to **make a change** (add, edit, fix, create, refactor anything) — call `assess_task` once first:\n" +
        "- If `recommendation` is **\"proceed\"**: implement immediately.\n" +
        "- If `recommendation` is **\"plan\"**: the system switches to plan mode automatically — do NOT call any other tool.\n\n" +
        "**Use \"plan\" for**: new features, new files, adding backend to a frontend-only app, database changes, anything described as a \"revamp\" or major change.\n" +
        "**Use \"proceed\" for**: bug fixes, style tweaks, copy changes, single-function edits.";

    public int MaxToolIterations => 20;

    /// <summary>Allow Read, Write, Assessment, and ModeSwitch tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) =>
        tool.Capability != ToolCapability.Planning;
}
