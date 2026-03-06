using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Plan mode: read-only + planning tools only, no write access.
/// The AI discusses and proposes a plan; no files are written.
/// </summary>
public sealed class PlanMode : IConversationMode
{
    public string Name => "plan";

    public string SystemPromptSuffix =>
        "\n\n## CURRENT MODE: Plan\n" +
        "You are in **plan mode**. Your ONLY action tool is `propose_plan`.\n" +
        "When the user asks to add, change, or remove something — call `propose_plan` with an UPDATED plan. Do NOT read or list files to investigate.\n" +
        "Read tools (read_file, list_files, search_file) are available only when the user explicitly asks to inspect existing code.\n" +
        "Write tools are BLOCKED — calling them will fail every time. Do NOT retry.\n" +
        "When the plan is ready, tell the user: 'Click **Implement Plan** to start, or switch to Agent mode.'";

    public int MaxToolIterations => 5;

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Write;
}
