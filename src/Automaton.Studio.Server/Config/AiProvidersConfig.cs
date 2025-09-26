using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Config;

public class AiProvidersConfig
{
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
    public static AiProviderConfig? GetProviderConfig(this AiProvidersConfig config, AiProvider provider)
    {
        var providerKey = provider.ToString();
        return config.Providers.TryGetValue(providerKey, out var providerConfig) ? providerConfig : null;
    }
}