namespace Automaton.Api.Config;

/// <summary>
/// File-system and URL settings for public published web apps.
/// </summary>
public class PublishedAppsConfig
{
    /// <summary>
    /// Folder where the API exports public published app packages.
    /// Relative paths are resolved under the API content root.
    /// </summary>
    public string RootPath { get; set; } = "AppData/published-apps";

    /// <summary>
    /// Public base URL for app links, for example https://apps.automaton.monster.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public string PublicBaseUrlHost =>
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
            ? uri.Host
            : "localhost";

    /// <summary>
    /// API base URL injected into published apps so they can call Automaton runtime APIs.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://localhost:7091";

    /// <summary>
    /// Shared secret that the runtime proxy must send to internal runtime auth endpoints.
    /// </summary>
    public string RuntimeProxyAuthKey { get; set; } = string.Empty;

    /// <summary>
    /// Folder where the API writes Traefik dynamic route files for managed runtime backends.
    /// Relative paths are resolved under the API content root.
    /// </summary>
    public string TraefikDynamicConfigRoot { get; set; } = "AppData/traefik-dynamic";

}
