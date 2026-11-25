using Automaton.Api.Config;

namespace Automaton.Api.Mcp;

/// <summary>
/// The full https address an MCP caller can open for an app, built from the apps domain the server is configured with
/// (the same host layout the CLI shows): the address name's host while the app is public, else the app's private host.
/// </summary>
internal static class AppMcpUrls
{
    internal static string? For(PublishedAppsConfig? config, Guid appId, string? slug, bool isPublic)
    {
        if (config is null || !Uri.TryCreate(config.PublicBaseUrl, UriKind.Absolute, out var baseUri)) return null;
        var host = isPublic && PublishedAppHostNames.IsValidPublishedSlug(slug ?? string.Empty)
            ? PublishedAppHostNames.BuildPublishedAppHost(slug!, baseUri.Host)
            : PublishedAppHostNames.BuildPrivateAppHost(appId, baseUri.Host);
        var port = baseUri.IsDefaultPort ? string.Empty : $":{baseUri.Port}";
        return $"{baseUri.Scheme}://{host}{port}/";
    }
}
