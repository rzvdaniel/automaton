namespace Automaton.Studio.Server.Config;

public class AiProvidersConfig
{
    /// <summary>
    /// Maximum number of tool call iterations before stopping.
    /// Default: 20
    /// </summary>
    public int MaxIterations { get; set; } = 20;

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