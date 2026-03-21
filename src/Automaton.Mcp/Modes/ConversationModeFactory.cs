namespace Automaton.Mcp.Modes;

/// <summary>
/// Resolves a mode name string to its <see cref="IConversationMode"/> implementation.
/// There is only one mode (agent). Plan mode has been removed — the AI decides when
/// to call propose_plan based on task complexity.
/// </summary>
public static class ConversationModeFactory
{
    private static readonly IConversationMode _agent = new AgentMode();

    /// <summary>
    /// Always returns <see cref="AgentMode"/>. The mode parameter is ignored for backwards compatibility.
    /// </summary>
    public static IConversationMode From(string? modeName) => _agent;
}
