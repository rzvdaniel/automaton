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
        "You are in **plan mode**. Your primary action is `propose_plan`.\n" +
        "You may use read tools (read_file, list_files, search_file) to understand existing code, but do NOT loop on them.\n" +
        "When the user asks to add, change, or remove something from the plan — call `propose_plan` with an UPDATED plan.\n" +
        "Write tools are BLOCKED — calling them will fail every time. Do NOT retry.\n" +
        "When the plan is ready, tell the user: 'Click **Implement Plan** to start, or switch to Agent mode.'";

    public int MaxToolIterations => 5;

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Write;
}
