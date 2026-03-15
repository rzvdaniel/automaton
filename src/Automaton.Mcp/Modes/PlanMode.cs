using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Plan mode: read-only + planning tools only, no write access.
/// The AI discusses and proposes a plan; no files are written.
/// </summary>
public sealed class PlanMode : IConversationMode
{
    public string Name => ConversationModes.Plan;

    public string SystemPromptSuffix =>
        "\n\n## CURRENT MODE: Plan\n" +
        "You are in **plan mode**. Your primary action is `propose_plan`.\n" +
        "You may use read tools (read_file, list_files, search_file) to understand existing code, but do NOT loop on them.\n" +
        "IMPORTANT: When the user asks to add or change something, call `propose_plan` with the COMPLETE plan — include ALL existing stages plus the new changes. Never submit a partial plan with only the new addition.\n" +
        "After calling `propose_plan`, ALWAYS reply with a short message. For the first plan: 'Click **Implement Plan** when ready.' For updates: describe what changed (e.g. 'I've added a footer section as Stage 4').\n" +
        "Write tools are BLOCKED — calling them will fail every time. Do NOT retry.\n\n" +
        "## TECHNICAL DECISIONS — THINK DEEPLY:\n" +
        "The `techDecisions` field is the most important part of the plan. The implementation model that follows your plan is less capable — it will blindly use whatever library or approach you specify. You must do the research and reasoning upfront.\n\n" +
        "For every non-trivial technical choice, ask yourself:\n" +
        "- **Python version compatibility**: does this library work on Python 3.13+? (e.g. `pydub` depends on `pyaudioop` which was removed in 3.13 — use `subprocess` + ffmpeg instead)\n" +
        "- **Binary dependencies**: does this require a system binary (ffmpeg, imagemagick, etc.)? Is it available on the runner?\n" +
        "- **Data transfer**: how is data passed from frontend to backend? (files must be base64-encoded strings — the platform does not support binary uploads via callController)\n" +
        "- **Exact implementation**: specify the exact Python stdlib or third-party approach, method names, and any gotchas\n\n" +
        "Each `techDecision` must be specific and actionable — not 'use pydub for audio' but 'use subprocess to call ffmpeg directly (`subprocess.run([\"ffmpeg\", \"-i\", input, output])`) — pydub is incompatible with Python 3.13+'.";

    public int MaxToolIterations => 5;

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) => tool.Capability != ToolCapability.Write;
}
