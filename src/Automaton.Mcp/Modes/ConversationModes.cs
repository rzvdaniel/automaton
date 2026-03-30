namespace Automaton.Mcp.Modes;

/// <summary>
/// Well-known conversation mode identifiers.
/// </summary>
public static class ConversationModes
{
    /// <summary>
    /// Autonomous mode — AI implements the entire task without pausing for user approval.
    /// No plan review, no stage-by-stage confirmation. Maximum iterations configurable via admin.
    /// </summary>
    public const string Autonomous = "autonomous";

    /// <summary>
    /// Human-in-the-middle mode — AI proposes a plan, waits for user confirmation,
    /// then implements.
    /// </summary>
    public const string HumanInTheMiddle = "human-in-the-middle";
}
