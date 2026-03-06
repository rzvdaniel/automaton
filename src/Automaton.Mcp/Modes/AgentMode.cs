using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Agent mode: full write access, no planning tools.
/// The plan has been approved — the AI implements directly.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => "agent";

    public string SystemPromptSuffix =>
        "\n\n## CURRENT MODE: Agent\n" +
        "You are in **agent mode**. The plan has been approved — proceed directly with implementation.\n" +
        "`propose_plan` and `stage_complete` are NOT available. Do not call them under any circumstances.\n" +
        "Use write_file / edit_file / list_files to implement the solution.";

    /// <summary>Allow Read and Write tools; block Planning tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Planning;
}
