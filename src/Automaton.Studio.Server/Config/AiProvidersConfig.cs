using Automaton.Mcp.SystemPrompts;

namespace Automaton.Studio.Server.Config;

public class AiProvidersConfig
{
    /// <summary>
    /// Maximum number of LLM round-trips (iterations) before stopping.
    /// 0 = use tier-based defaults defined in <see cref="IterationLimits"/>.
    /// Set a positive value to override for all tiers.
    /// </summary>
    public int MaxIterations { get; set; } = 0;

    /// <summary>
    /// Maximum consecutive failures allowed for the same tool before stopping.
    /// Default: 3
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Per-tier iteration limits. Zero values fall back to the built-in defaults
    /// in <see cref="Automaton.Mcp.SystemPrompts.IterationLimits"/>.
    /// </summary>
    public IterationLimitsConfig IterationLimits { get; set; } = new();

    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();
}

/// <summary>
/// Per-complexity-tier maximum iteration overrides. 0 = use the hardcoded default.
/// </summary>
public class IterationLimitsConfig
{
    public int Simple     { get; set; } = 0;
    public int Medium     { get; set; } = 0;
    public int Large      { get; set; } = 0;
    public int Enterprise { get; set; } = 0;

    /// <summary>Returns the configured override for <paramref name="tier"/>, or 0 if not set.</summary>
    public int For(ComplexityTier? tier) => tier switch
    {
        ComplexityTier.Simple     => Simple,
        ComplexityTier.Medium     => Medium,
        ComplexityTier.Large      => Large,
        ComplexityTier.Enterprise => Enterprise,
        _                         => Medium
    };
}

public class AiProviderConfig
{
    public string DefaultModel { get; set; } = string.Empty;
    public List<string> SupportedModels { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public static class AiProvidersConfigExtensions
{
    public static AiProviderConfig? GetProviderConfig(this AiProvidersConfig config, string providerName)
    {
        return config.Providers.TryGetValue(providerName, out var providerConfig) ? providerConfig : null;
    }
}