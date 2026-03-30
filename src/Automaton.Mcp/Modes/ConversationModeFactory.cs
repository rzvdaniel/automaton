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
    /// </summary>
    public static IConversationMode From(string? modeName) => modeName switch
    {
        ConversationModes.Autonomous      => _autonomous,
        ConversationModes.HumanInTheMiddle => _humanInTheMiddle,
        _                                 => _autonomous,
    };
}
