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
        "After calling `propose_plan`, ALWAYS reply with a short message asking if the user wants to implement it. For example: 'The plan is ready — shall I implement it?' The UI will automatically switch to Agent mode so the user can confirm with a single reply.\n" +
        "Write tools are BLOCKED — calling them will fail every time. Do NOT retry.\n\n" +
        "## TECHNICAL DECISIONS — THINK DEEPLY:\n" +
        "The `techDecisions` field is the most important part of the plan. The implementation model that follows your plan is less capable — it will blindly use whatever library or approach you specify. You must do the research and reasoning upfront.\n\n" +
        "For every non-trivial technical choice, ask yourself:\n" +
        "- **Python version compatibility**: does this library work on Python 3.13+? (e.g. `pydub` depends on `pyaudioop` which was removed in 3.13 — use `subprocess` + ffmpeg instead)\n" +
        "- **Binary dependencies**: does this require a system binary (ffmpeg, imagemagick, etc.)? Is it available on the runner?\n" +
        "- **Data transfer**: how is data passed from frontend to backend? Use `automatonApi.uploadFile(file)` for file uploads — the file streams to the runner's volume and the session_id is passed to the controller. NEVER use base64 encoding for files.\n" +
        "- **File transfer**: server is a pure proxy — files only reside on runner volume. Use uploadFile() + session_id for uploads, getDownloadUrl(session_id) for downloads. Python controller reads from /tmp/uploads/{session_id}/, writes results to /tmp/results/{session_id}/.\n" +
        "- **Exact implementation**: specify the exact Python stdlib or third-party approach, method names, and any gotchas\n\n" +
        "Each `techDecision` must be specific and actionable — not 'use pydub for audio' but 'use subprocess to call ffmpeg directly (`subprocess.run([\"ffmpeg\", \"-i\", input, output])`) — pydub is incompatible with Python 3.13+'.";

    public int MaxToolIterations => 5;

    /// <summary>Allow Read and Planning tools; block Write tools.</summary>
    public bool IsToolAllowed(McpTool tool) =>
        tool.Capability != ToolCapability.Write;
}
