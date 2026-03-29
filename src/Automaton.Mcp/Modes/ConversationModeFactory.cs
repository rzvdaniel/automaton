namespace Automaton.Mcp.Modes;

/// <summary>
/// Resolves a mode name string to its <see cref="IConversationMode"/> implementation.
/// </summary>
public static class ConversationModeFactory
{
    private static readonly IConversationMode _autonomous       = new AutonomousMode();
    private static readonly IConversationMode _humanInTheMiddle = new HumanInTheMiddleMode();

    /// <summary>
    /// Returns the <see cref="IConversationMode"/> for <paramref name="modeName"/>.
    /// Defaults to <see cref="AutonomousMode"/> when the mode is null or unrecognised.
    /// Legacy "agent" and "plan" values map to <see cref="HumanInTheMiddleMode"/>.
    /// </summary>
    public static IConversationMode From(string? modeName) => modeName switch
    {
        ConversationModes.Autonomous                               => _autonomous,
        ConversationModes.HumanInTheMiddle                        => _humanInTheMiddle,
        ConversationModes.Agent or ConversationModes.Plan         => _humanInTheMiddle,
        _                                                         => _autonomous,
    };
}
