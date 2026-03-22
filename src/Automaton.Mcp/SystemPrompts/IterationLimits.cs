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
    /// Returns the tier-appropriate default, or <paramref name="configuredValue"/> if the
    /// caller explicitly overrode it (positive non-zero value).
    /// </summary>
    public static int Resolve(ComplexityTier? tier, int configuredValue = 0)
    {
        if (configuredValue > 0)
            return configuredValue;

        return tier switch
        {
            ComplexityTier.Simple     => Simple,
            ComplexityTier.Large      => Large,
            ComplexityTier.Enterprise => Enterprise,
            _                         => Medium   // null or Medium
        };
    }
}
