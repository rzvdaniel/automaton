namespace Automaton.Mcp.Modes;

/// <summary>
/// Resolves a mode name string to its <see cref="IConversationMode"/> implementation.
/// Add new modes here — callers need no changes.
/// </summary>
public static class ConversationModeFactory
{
    private static readonly IConversationMode _agent = new AgentMode();
    private static readonly IConversationMode _plan  = new PlanMode();

    /// <summary>
    /// Returns the mode for the given name. Defaults to <see cref="AgentMode"/> for unknown values.
    /// </summary>
    public static IConversationMode From(string? modeName) => modeName?.ToLowerInvariant() switch
    {
        ConversationModes.Plan  => _plan,
        ConversationModes.Agent => _agent,
        _                       => _agent,   // safe default
    };
}
