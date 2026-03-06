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
        "You are in **plan mode**. Discuss requirements and propose plans using `propose_plan`. That is all.\n" +
        "write_file, edit_file, delete_file and all write tools are BLOCKED — calling them will fail every time.\n" +
        "Do NOT retry blocked tools. Do NOT try to escape plan mode yourself.\n" +
        "When the plan is ready and the user wants to implement, tell them: 'Click **Implement Plan** to start, or switch to Agent mode.'";

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Write;
}
