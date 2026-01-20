namespace Automaton.Studio.Server.Config;

/// <summary>
/// Configuration for the system-level AI service.
/// This service uses Automaton's own API key to provide AI capabilities
/// to all users within their token quotas.
/// </summary>
public class SystemAiConfig
{
    /// <summary>
    /// The default AI provider to use (e.g., "OpenAI", "Anthropic")
    /// </summary>
    public string DefaultProvider { get; set; } = "OpenAI";

    /// <summary>
    /// The default model to use (e.g., "gpt-4o-mini", "claude-3-haiku")
    /// Cost-effective models are recommended for utility tasks.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// The system API key for the default provider.
    /// This key is owned by Automaton and shared across all users.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum requests per user per minute (rate limiting)
    /// </summary>
    public int MaxRequestsPerUserPerMinute { get; set; } = 10;

    /// <summary>
    /// Whether the system AI service is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}
