using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Autonomous mode: AI implements the entire task from start to finish without pausing
/// for user approval. No plan review, no stage-by-stage confirmation.
/// Iteration limit defaults to <see cref="IterationLimits.Autonomous"/> (500) unless
/// overridden by admin configuration.
/// </summary>
public sealed class AutonomousMode : IConversationMode
{
    public string Name => ConversationModes.Autonomous;

    public string SystemPromptSuffix =>
        "\n\n## RULES\n" +
        "You are operating in AUTONOMOUS mode. Implement everything from start to finish without pausing.\n\n" +
        "- NEVER call `propose_plan` — implement directly without seeking approval.\n" +
        "- NEVER ask the user whether to proceed between stages.\n" +
        "- Work through ALL files and stages in a single uninterrupted pass.\n" +
        "- Do NOT narrate individual writes. Work silently.\n" +
        "- Write ONE summary message when everything is done, listing what was created/changed.\n\n" +
        "For complex tasks: analyse the requirements internally, then implement everything — " +
        "all files, all stages — without stopping.\n\n" +
        "If the user asks a question: answer in plain text, do not touch any files.\n\n" +
        "Before deleting any file: always call delete_file WITHOUT confirmed=true first, then ask the user to confirm. Only call it with confirmed=true after they approve.";

    public int MaxToolIterations => 20;

    public bool IsToolAllowed(McpTool tool) => true;
}
