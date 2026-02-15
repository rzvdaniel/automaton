namespace Automaton.Studio.Server.Config;

/// <summary>
/// Configuration for published web apps caching and serving
/// </summary>
public class WebAppConfig
{
    /// <summary>
    /// Cache max-age in seconds for published app static files (CSS, JS, images)
    /// Default: 86400 (24 hours)
    /// </summary>
    public int CacheMaxAgeSeconds { get; set; } = 86400;
}
