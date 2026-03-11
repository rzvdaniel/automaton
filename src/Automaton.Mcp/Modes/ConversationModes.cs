namespace Automaton.Mcp.Modes;

/// <summary>
/// Well-known conversation mode identifiers.
/// </summary>
public static class ConversationModes
{
    /// <summary>Read-only planning mode — <c>propose_plan</c> is the primary action; write tools are blocked.</summary>
    public const string Plan = "plan";

    /// <summary>Full agent mode — write tools enabled; <c>propose_plan</c> is not used.</summary>
    public const string Agent = "agent";
}
