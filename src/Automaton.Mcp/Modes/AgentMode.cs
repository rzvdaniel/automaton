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
        "\n\n## COMPLEXITY ASSESSMENT — DO THIS FIRST\n" +
        "Before taking any action, assess whether the task is simple or complex:\n\n" +
        "**If the user is asking a question or requesting an explanation — answer directly. Do NOT call `request_plan`.**\n\n" +
        "**Proceed directly** (read + write tools) for:\n" +
        "- Bug fixes or small corrections\n" +
        "- Adding/editing a single UI element or function\n" +
        "- Straightforward changes where the approach is obvious\n\n" +
        "**Call `request_plan` first** for:\n" +
        "- New multi-file features or complete new sections\n" +
        "- Architectural decisions (data flow, component structure, libraries)\n" +
        "- Tasks with significant unknowns that require reading the codebase first\n" +
        "- Anything that would benefit from staged, user-approved implementation\n\n" +
        "After calling `request_plan`, STOP — do not call any other tools. The system restarts in plan mode automatically.";

    public int MaxToolIterations => 20;

    /// <summary>Allow Read, Write, and ModeSwitch tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) =>
        tool.Capability != ToolCapability.Planning;
}
