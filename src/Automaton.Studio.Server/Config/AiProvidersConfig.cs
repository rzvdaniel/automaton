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

    public AppMemoryConfig AppMemory { get; set; } = new();

    public AppInstructionsConfig AppInstructions { get; set; } = new();

    public RateLimitRetryConfig RateLimitRetry { get; set; } = new();
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

/// <summary>
/// Configures the User Instructions block (.instructions/ files) injected into the system prompt.
/// </summary>
public class AppInstructionsConfig
{
    /// <summary>
    /// Maximum tokens to inject from .instructions/ files. Default: 1000 (~4000 chars).
    /// Increase for large/enterprise apps with detailed instruction sets.
    /// </summary>
    public int MaxTokens { get; set; } = 1000;
}

/// <summary>
/// Configures the App Memory Block injected into the system prompt for existing apps.
/// </summary>
public class AppMemoryConfig
{
    /// <summary>
    /// Maximum characters for each individual version summary. Default: 200.
    /// </summary>
    public int SummaryMaxChars { get; set; } = 200;

    /// <summary>
    /// Hard character cap for the entire memory block. Default: 2400 (~600 tokens).
    /// </summary>
    public int BlockMaxChars { get; set; } = 2400;
}

/// <summary>
/// Configures rate-limit retry behaviour for AI provider calls.
/// </summary>
public class RateLimitRetryConfig
{
    /// <summary>
    /// Maximum number of retries on a rate-limit response. Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay in seconds for exponential backoff. Subsequent retries double this value.
    /// Default: 64s  (64 → 128 → 256). Minimum useful value for Anthropic Tier 1 (50K TPM limit).
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 64;
}

public static class AiProvidersConfigExtensions
{
    public static AiProviderConfig? GetProviderConfig(this AiProvidersConfig config, string providerName)
    {
        return config.Providers.TryGetValue(providerName, out var providerConfig) ? providerConfig : null;
    }
}