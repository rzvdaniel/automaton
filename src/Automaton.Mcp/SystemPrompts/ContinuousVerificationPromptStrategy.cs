namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Verification-heavy policy: frequent checks while implementing.
/// </summary>
public sealed class ContinuousVerificationPromptStrategy : IAppVerificationPromptStrategy
{
    public string Name => AppVerificationStrategyNames.Continuous;

    public string BuildPromptBlock() =>
        """
## VERIFICATION STRATEGY (CONTINUOUS)
- Use ui_* checks throughout implementation (after significant UI changes and before completion).
- Run ui_get_console_logs regularly with focused args.
- If blocking errors are detected, fix immediately and re-check before proceeding.
""";
}
