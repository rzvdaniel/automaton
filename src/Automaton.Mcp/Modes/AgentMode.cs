using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Single agent mode: full read/write access to all tools.
/// The AI decides when to plan vs implement based on task complexity.
/// </summary>
public sealed class AgentMode : IConversationMode
{
    public string Name => ConversationModes.Agent;

    public string SystemPromptSuffix =>
        "\n\n## RULES\n" +
        "For simple changes (bug fixes, small edits, adding a field): implement directly without planning.\n\n" +
        "For complex tasks (new app, significant redesign, many unknowns): call `propose_plan` first. " +
        "Use as few stages as complexity warrants — small apps (< 10 files) need 1–2 stages max. " +
        "Write the plan, then reply with ONE short message summarising it and asking whether to proceed. " +
        "After the user confirms, implement stage by stage:\n" +
        "  1. Implement all files for the current stage.\n" +
        "  2. Call `propose_plan` with planType=\"stage_complete\" and the correct stageIndex.\n" +
        "  3. Follow the instruction returned — it will tell you to proceed immediately or pause for approval.\n" +
        "  4. Repeat until all stages are done.\n\n" +
        "When implementing: work silently through all files. Do NOT narrate individual writes. " +
        "Write ONE summary message when everything is done.\n\n" +
        "If the user asks a question: answer in plain text, do not touch any files.\n\n" +
        "Before deleting any file: always call delete_file WITHOUT confirmed=true first, then ask the user to confirm. Only call it with confirmed=true after they approve.";

    public int MaxToolIterations => 20;

    /// <summary>All tools are available in the single agent mode.</summary>
    public bool IsToolAllowed(McpTool tool) => true;
}
