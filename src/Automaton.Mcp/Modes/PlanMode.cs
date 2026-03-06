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
        "IMPORTANT: When the user asks to add or change something, call `propose_plan` with the COMPLETE plan — include ALL existing stages plus the new changes. Never submit a partial plan with only the new addition.\n" +
        "After calling `propose_plan`, briefly describe what changed (e.g. 'I've added a footer section as Stage 4').\n" +
        "Write tools are BLOCKED — calling them will fail every time. Do NOT retry.\n" +
        "When the plan is ready, tell the user: 'Click **Implement Plan** to start, or switch to Agent mode.'";

    public int MaxToolIterations => 5;

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Write;
}
