namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Balanced policy: one mid-run checkpoint plus final hard gate.
/// </summary>
public sealed class BalancedVerificationPromptStrategy : IAppVerificationPromptStrategy
{
    public string Name => AppVerificationStrategyNames.Balanced;

    public string BuildPromptBlock() =>
        """
## VERIFICATION STRATEGY (BALANCED)
- Avoid excessive on-the-fly verification loops.
- Prefer one mid-run checkpoint after core functionality is wired, then a final verification gate.
- Mid-run checkpoint: use focused checks (e.g., ui_wait_for/ui_assert + ui_get_console_logs warn+).
- Final gate: run ui_get_console_logs with error-level; blocking errors must be fixed before completion.
""";
}
