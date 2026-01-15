using Automaton.Mcp.Client.Providers;

namespace Automaton.Mcp.Client;

/// <summary>
/// Interface for the AI provider registry that manages available provider implementations
/// </summary>
public interface IAiProviderRegistry
{
    /// <summary>
    /// Gets all registered providers keyed by their ProviderName (case-insensitive)
    /// </summary>
    IReadOnlyDictionary<string, IAiProvider> Providers { get; }

    /// <summary>
    /// Gets a provider by name (case-insensitive)
    /// </summary>
    /// <param name="providerName">The provider name (e.g., "OpenAI", "Anthropic")</param>
    /// <returns>The provider if found, null otherwise</returns>
    IAiProvider? GetProvider(string providerName);

    /// <summary>
    /// Checks if a provider with the given name is implemented
    /// </summary>
    /// <param name="providerName">The provider name to check</param>
    /// <returns>True if the provider is implemented, false otherwise</returns>
    bool IsImplemented(string providerName);

    /// <summary>
    /// Gets the names of all implemented providers
    /// </summary>
    IEnumerable<string> GetImplementedProviderNames();
}

/// <summary>
/// Registry that holds all registered AI provider implementations
/// </summary>
public class AiProviderRegistry : IAiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IAiProvider> Providers => _providers;

    /// <summary>
    /// Registers a provider implementation
    /// </summary>
    /// <param name="provider">The provider to register</param>
    public void Register(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderName))
        {
            throw new ArgumentException("Provider must have a non-empty ProviderName", nameof(provider));
        }

        _providers[provider.ProviderName] = provider;
    }

    /// <inheritdoc />
    public IAiProvider? GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        return _providers.TryGetValue(providerName, out var provider) ? provider : null;
    }

    /// <inheritdoc />
    public bool IsImplemented(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        return _providers.ContainsKey(providerName);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetImplementedProviderNames()
    {
        return _providers.Keys.ToList();
    }
}
