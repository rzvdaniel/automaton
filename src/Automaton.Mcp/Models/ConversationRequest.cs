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
    /// Defaults to 4096 if not set.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

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
    /// Interaction mode: <see cref="ConversationModes.Plan"/> (read-only, propose_plan available)
    /// or <see cref="ConversationModes.Agent"/> (write tools, default).
    /// </summary>
    public string Mode { get; set; } = ConversationModes.Agent;
}
