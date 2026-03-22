namespace Automaton.Studio.Server.Config;

public class AiProvidersConfig
{
    /// <summary>
    /// Maximum number of LLM round-trips (iterations) before stopping.
    /// 0 = use tier-based defaults (Simple: 15, Medium: 25, Large: 40, Enterprise: 60).
    /// Set a positive value to override for all tiers.
    /// </summary>
    public int MaxIterations { get; set; } = 0;

    /// <summary>
    /// Maximum consecutive failures allowed for the same tool before stopping.
    /// Default: 3
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();
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