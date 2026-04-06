using Automaton.Mcp.SystemPrompts;

namespace Automaton.Studio.Server.Config;

public class AiProvidersConfig
{
    /// <summary>
    /// Maximum number of conversation history messages included per request.
    /// Higher values improve multi-turn coherence but increase token usage.
    /// </summary>
    public int MaxConversationHistoryMessages { get; set; } = 30;

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
    /// Default maximum output tokens per response turn. Used in two places:
    /// (1) as the default when a new AI model is created via the admin API;
    /// (2) as the runtime fallback when the model is not found in the database.
    /// The per-model value stored in <c>AiModel.MaxTokens</c> always takes precedence at runtime.
    /// Default: 65536 (64K) — covers all modern frontier models including Claude 3.7 and Gemini 2.5 Pro.
    /// </summary>
    public int DefaultMaxOutputTokens { get; set; } = 65536;

    /// <summary>
    /// Default verification strategy for app generation prompts.
    /// Allowed values: continuous, balanced, final-gate.
    /// </summary>
    public string AppVerificationDefaultStrategy { get; set; } = AppVerificationStrategyNames.Balanced;

    /// <summary>
    /// Whether users can override the default verification strategy in personal settings.
    /// </summary>
    public bool AppVerificationAllowUserOverride { get; set; } = true;

    /// <summary>
    /// Per-tier iteration limits. Zero values fall back to the built-in defaults
    /// in <see cref="Automaton.Mcp.SystemPrompts.IterationLimits"/>.
    /// </summary>
    public IterationLimitsConfig IterationLimits { get; set; } = new();

    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();

    public AppMemoryConfig AppMemory { get; set; } = new();

    public AppInstructionsConfig AppInstructions { get; set; } = new();

    public RateLimitRetryConfig RateLimitRetry { get; set; } = new();

    public StreamingConfig Streaming { get; set; } = new();

    /// <summary>
    /// Controls server-side history compaction when conversation history exceeds
    /// <see cref="MaxConversationHistoryMessages"/>.
    /// </summary>
    public HistoryCompactionConfig HistoryCompaction { get; set; } = new();
}

/// <summary>
/// The subset of <see cref="AiProvidersConfig"/> that can be modified at runtime
/// via the admin UI and is persisted in <c>Settings["AiProviders"]</c> as JSON.
/// Infrastructure config (Providers, AppMemory, AppInstructions, RateLimitRetry, Streaming)
/// stays in appsettings.json only.
/// </summary>
public class AiProvidersOperationalConfig
{
    public int MaxConversationHistoryMessages { get; set; } = 30;
    public int MaxIterations                  { get; set; } = 0;
    public int MaxConsecutiveFailures         { get; set; } = 3;
    public int DefaultMaxOutputTokens         { get; set; } = 65536;
    public string AppVerificationDefaultStrategy { get; set; } = AppVerificationStrategyNames.Balanced;
    public bool AppVerificationAllowUserOverride { get; set; } = true;
    public IterationLimitsConfig IterationLimits { get; set; } = new();
    public HistoryCompactionConfig HistoryCompaction { get; set; } = new();
}

public class HistoryCompactionConfig
{
    /// <summary>
    /// Number of latest raw messages kept after compaction (plus one summary message).
    /// </summary>
    public int RecentMessages { get; set; } = HistoryCompactionDefaults.RecentMessages;

    /// <summary>
    /// Maximum number of summarized bullet entries generated from older turns.
    /// </summary>
    public int MaxSummaryEntries { get; set; } = HistoryCompactionDefaults.MaxSummaryEntries;

    /// <summary>
    /// Maximum total characters of the generated summary block.
    /// </summary>
    public int MaxSummaryChars { get; set; } = HistoryCompactionDefaults.MaxSummaryChars;

    /// <summary>
    /// Maximum characters per summarized entry.
    /// </summary>
    public int MaxEntryChars { get; set; } = HistoryCompactionDefaults.MaxEntryChars;
}

public static class HistoryCompactionDefaults
{
    public const int RecentMessages = 12;
    public const int MaxSummaryEntries = 30;
    public const int MaxSummaryChars = 3500;
    public const int MaxEntryChars = 220;
}

public static class HistoryCompactionBounds
{
    public const int MinRecentMessages = 1;
    public const int MaxRecentMessages = 100;

    public const int MinMaxSummaryEntries = 1;
    public const int MaxMaxSummaryEntries = 200;

    public const int MinMaxSummaryChars = 500;
    public const int MaxMaxSummaryChars = 10000;

    public const int MinMaxEntryChars = 50;
    public const int MaxMaxEntryChars = 1000;
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

    /// <summary>
    /// Override for Autonomous mode. 0 = use the built-in default (500).
    /// </summary>
    public int Autonomous { get; set; } = 0;

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
    /// Default: 30s  (30 → 60 → 120). The provider will use the <c>retry-after</c> header
    /// from Anthropic's 429 response when available, overriding this value.
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 30;
}

/// <summary>
/// Configures streaming behaviour for AI provider calls.
/// </summary>
public class StreamingConfig
{
    /// <summary>
    /// Maximum minutes to wait for a single streaming call to complete before cancelling.
    /// Protects against providers that open the stream but never deliver chunks or close it.
    /// Default: 5 minutes.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 5;
}

public static class AiProvidersConfigExtensions
{
    public static AiProviderConfig? GetProviderConfig(this AiProvidersConfig config, string providerName)
    {
        return config.Providers.TryGetValue(providerName, out var providerConfig) ? providerConfig : null;
    }
}