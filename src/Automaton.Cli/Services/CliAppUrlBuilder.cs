namespace Automaton.Cli.Services;

public static class CliAppUrlBuilder
{
    public static string BuildDeployedUrl(string? server, string? appId)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        return $"{ResolveAppsBaseUrl(server).TrimEnd('/')}/private/{Uri.EscapeDataString(appId.Trim())}/";
    }

    public static string BuildPublishedUrl(string server, string slug)
    {
        return $"{ResolveAppsBaseUrl(server).TrimEnd('/')}/{Uri.EscapeDataString(slug.Trim())}/";
    }

    private static string ResolveAppsBaseUrl(string server)
    {
        var configuredAppsBaseUrl = Environment.GetEnvironmentVariable("AUTOMATON_APPS_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configuredAppsBaseUrl))
        {
            return configuredAppsBaseUrl.TrimEnd('/');
        }

        if (Uri.TryCreate(server, UriKind.Absolute, out var serverUri) &&
            serverUri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(serverUri)
            {
                Host = "apps." + serverUri.Host["api.".Length..],
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return server.TrimEnd('/');
    }
}
