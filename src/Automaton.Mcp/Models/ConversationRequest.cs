using Automaton.Mcp.Modes;

namespace Automaton.Mcp.Models;

/// <summary>
/// Carries the system prompt split into stable and dynamic parts for provider-level caching.
/// AnthropicProvider places cache_control only on <see cref="BasePrompt"/> (explicit caching).
/// OpenAiCompatibleProvider sends each part as a separate SystemChatMessage so OpenAI's
/// automatic prefix caching hits on the stable <see cref="BasePrompt"/> without being
/// invalidated by per-app instructions or per-session memory.
/// Other providers receive the concatenated <see cref="Full"/> string.
/// </summary>
public record SystemPromptParts(string BasePrompt, string? InstructionBlock = null, string? MemoryBlock = null)
{
    /// <summary>All parts joined for providers that don't support split system messages.</summary>
    public string Full =>
        string.Join("\n\n", new[] { BasePrompt, InstructionBlock, MemoryBlock }
            .Where(p => !string.IsNullOrWhiteSpace(p))!);
}

/// <summary>
/// Request to start a conversation with the AI
/// </summary>
public class ConversationRequest
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string InitialMessage { get; set; } = string.Empty;

    /// <summary>
    /// Flat system prompt string. Used by providers that don't support split system messages.
    /// Prefer setting <see cref="SystemPromptParts"/> for Anthropic to enable prompt caching.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Split system prompt parts for provider-level prompt caching (Anthropic).
    /// When set, <see cref="SystemPrompt"/> is ignored by AnthropicProvider.
    /// </summary>
    public SystemPromptParts? SystemPromptParts { get; set; }

    /// <summary>
    /// Provider name string (e.g., "OpenAI", "Anthropic", "Google")
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>
    /// Maximum number of LLM round-trips before stopping.
    /// 0 = use <see cref="IterationLimits"/> tier-based defaults.
    /// </summary>
    public int MaxIterations { get; set; } = 0;

    /// <summary>
    /// Maximum consecutive failures allowed for the same tool before stopping.
    /// Prevents repeated failing tool calls.
    /// Default: 3
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Maximum output tokens per response turn. Sourced from the AiModel.MaxTokens database field.
    /// Defaults to <c>AiProvidersConfig.DefaultMaxOutputTokens</c> (configurable, default 32768) if not set.
    /// This field is always overridden by <c>AiConversationControllerBase</c> before the request is dispatched.
    /// </summary>
    public int MaxTokens { get; set; } = 65536;

    /// <summary>
    /// Maximum retries on a rate-limit response. Sourced from AiProviders:RateLimitRetry:MaxRetries.
    /// </summary>
    public int MaxRateLimitRetries { get; set; } = 3;

    /// <summary>
    /// Initial exponential backoff delay in seconds. Doubles on each retry (64 → 128 → 256).
    /// </summary>
    public int RateLimitRetryInitialDelaySeconds { get; set; } = 64;

    /// <summary>
    /// Maximum minutes to wait for a single streaming call to complete before cancelling.
    /// Protects against providers that open the stream but never deliver chunks or close it.
    /// Default: 5 minutes.
    /// </summary>
    public int StreamingTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Interaction mode: <see cref="ConversationModes.Autonomous"/> (default)
    /// or <see cref="ConversationModes.HumanInTheMiddle"/> (plan then implement).
    /// </summary>
    public string Mode { get; set; } = ConversationModes.Autonomous;

    /// <summary>
    /// Optional temperature override (0.0–2.0). Null means use the provider's default.
    /// Lower values (0.0–0.3) produce more focused, deterministic output.
    /// Higher values (0.7–1.0) produce more varied, creative output.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Optional top-p nucleus sampling override (0.0–1.0). Null means use the provider's default.
    /// Alternative to temperature — controls diversity by limiting the cumulative probability mass.
    /// </summary>
    public float? TopP { get; set; }

    /// <summary>
    /// Optional JSON object merged into the top-level request body for provider-specific parameters.
    /// Example: {"reasoning": {"enabled": true}} for OpenRouter reasoning models.
    /// Sourced from AiModel.ExtraBodyJson in the database.
    /// </summary>
    public string? ExtraBodyJson { get; set; }

    /// <summary>
    /// When false, tools are not sent to the model even if the tool provider has registered them.
    /// Set to false for reasoning-only models that reject requests containing a tools array.
    /// Sourced from AiModel.SupportsTools in the database. Defaults to true.
    /// </summary>
    public bool SupportsTools { get; set; } = true;
}
