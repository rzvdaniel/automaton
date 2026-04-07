namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Tool-efficient execution behavior focused on reducing API/tool churn.
/// </summary>
public sealed class LeanToolsExecutionPolicyPromptStrategy : IAppExecutionPolicyPromptStrategy
{
    public string Name => AppExecutionPolicyNames.LeanTools;

    public string BuildPromptBlock() =>
        """
## EXECUTION POLICY (LEAN-TOOLS)
- Minimize tool activity while preserving correctness.
- Batch related edits into fewer tool calls when practical.
- Avoid redundant read-after-write when tool output already confirms success.
- Avoid repetitive retries: after repeated failures, switch approach instead of looping.
- Prefer one boundary verification pass near completion unless a blocking runtime error appears.
""";
}
