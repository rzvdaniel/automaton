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
        "Before taking any action, decide: is this a **simple change** or a **complex feature**?\n\n" +
        "**Proceed directly** (read + write tools) for:\n" +
        "- Bug fixes or small corrections\n" +
        "- Editing a single UI element, style, or function\n" +
        "- Changing existing text, labels, or colors\n\n" +
        "**Call `request_plan` FIRST — no exceptions** for:\n" +
        "- Adding a new backend controller or Python file\n" +
        "- Adding a database (SQLite or any other) to an existing app\n" +
        "- Adding a feature that requires changes to 3 or more files\n" +
        "- Introducing any new user-facing section, screen, or flow (e.g. leaderboard, login, settings page)\n" +
        "- Architectural decisions: data models, API design, library selection\n" +
        "- Any task described as 'revamp', 'redesign', 'add persistence', or 'add authentication'\n\n" +
        "**Rule of thumb**: if you need to create any new file OR touch both frontend and backend, call `request_plan` first.\n\n" +
        "After calling `request_plan`, STOP — do not call any other tools. The system restarts in plan mode automatically.";

    public int MaxToolIterations => 20;

    /// <summary>Allow Read, Write, and ModeSwitch tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Planning;
}
