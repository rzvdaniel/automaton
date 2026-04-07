namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Minimal-verification policy: defer most checks to final gate.
/// </summary>
public sealed class FinalGateVerificationPromptStrategy : IAppVerificationPromptStrategy
{
    public string Name => AppVerificationStrategyNames.FinalGate;

    public string BuildPromptBlock() =>
        """
## VERIFICATION STRATEGY (FINAL-GATE)
- Minimize verification during implementation unless a visible runtime defect appears.
- Implement core behavior first, then run one consolidated verification pass near completion.
- Final pass must include ui_get_console_logs at error-level and required ui_assert checks.
- A ui_* response that only contains `clientExecutionRequired: true` is not a passed check; verification requires concrete runtime results.
- If blocking errors appear, enter fix/retest loop until clean.
""";
}
