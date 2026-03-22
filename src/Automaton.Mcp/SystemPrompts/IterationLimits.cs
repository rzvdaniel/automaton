namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Default maximum LLM iteration counts per complexity tier.
/// One iteration = one round-trip to the AI server (not one tool call).
/// </summary>
public static class IterationLimits
{
    public const int Simple     = 15;
    public const int Medium     = 25;
    public const int Large      = 40;
    public const int Enterprise = 60;

    /// <summary>
    /// Resolves the iteration limit using this priority order:
    /// 1. <paramref name="globalOverride"/> — positive value overrides everything
    /// 2. <paramref name="tierOverride"/> — positive value overrides for this tier only
    /// 3. Built-in tier constant (Simple/Medium/Large/Enterprise)
    /// </summary>
    public static int Resolve(ComplexityTier? tier, int globalOverride = 0, int tierOverride = 0)
    {
        if (globalOverride > 0) return globalOverride;
        if (tierOverride   > 0) return tierOverride;

        return tier switch
        {
            ComplexityTier.Simple     => Simple,
            ComplexityTier.Large      => Large,
            ComplexityTier.Enterprise => Enterprise,
            _                         => Medium   // null or Medium
        };
    }
}
