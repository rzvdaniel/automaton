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
    /// Default iteration limit for Autonomous mode. High enough to complete complex
    /// multi-file apps without human intervention.
    /// </summary>
    public const int Autonomous = 500;

    /// <summary>
    /// Resolves the iteration limit for HumanInTheMiddle (tier-based) mode:
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

    /// <summary>
    /// Resolves the iteration limit for Autonomous mode:
    /// 1. <paramref name="globalOverride"/> — positive admin cap overrides everything
    /// 2. <paramref name="autonomousOverride"/> — admin-configured autonomous limit
    /// 3. Built-in <see cref="Autonomous"/> constant (500)
    /// </summary>
    public static int ResolveForAutonomous(int globalOverride = 0, int autonomousOverride = 0)
    {
        if (globalOverride    > 0) return globalOverride;
        if (autonomousOverride > 0) return autonomousOverride;
        return Autonomous;
    }
}
