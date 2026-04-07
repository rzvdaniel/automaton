namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Baseline execution behavior for app generation.
/// </summary>
public sealed class DefaultExecutionPolicyPromptStrategy : IAppExecutionPolicyPromptStrategy
{
    public string Name => AppExecutionPolicyNames.Default;

    public string BuildPromptBlock() =>
        """
## EXECUTION POLICY (DEFAULT)
- Use tools as needed to implement robustly and verify outcomes.
- Prefer clarity and correctness over minimizing tool calls.
- Use read/write/verify loops when uncertain about runtime behavior.
""";
}
