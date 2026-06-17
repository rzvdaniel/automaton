namespace Automaton.Api.Config;

/// <summary>
/// Configuration for published web apps caching and serving
/// </summary>
public class WebAppConfig
{
    /// <summary>
    /// Cache max-age in seconds for published app assets that are clearly versioned
    /// (for example Vite/webpack fingerprinted files). HTML and non-versioned assets
    /// are revalidated so plain apps do not serve stale JS/CSS after publish.
    /// Default: 86400 (24 hours)
    /// </summary>
    public int CacheMaxAgeSeconds { get; set; } = 86400;
}
