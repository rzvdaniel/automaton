namespace Automaton.Cli.Services;

public static class CliAppUrlBuilder
{
    public static string BuildRestrictedAppUrl(string? server, string? appId)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        var appsBaseUrl = new Uri(ResolveAppsBaseUrl(server));
        var host = appsBaseUrl.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
            ? appsBaseUrl.Host["apps.".Length..]
            : appsBaseUrl.Host;
        var builder = new UriBuilder(appsBaseUrl)
        {
            Host = $"app-{appId.Trim().Replace("-", string.Empty, StringComparison.Ordinal)}.apps.{host}",
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

    public static string BuildRuntimeLaunchUrl(string? server, string? appId, string? runtimeToken)
    {
        var deployedUrl = BuildRestrictedAppUrl(server, appId);
        if (string.IsNullOrWhiteSpace(deployedUrl) || string.IsNullOrWhiteSpace(runtimeToken))
        {
            return string.Empty;
        }

        return $"{deployedUrl}?token={Uri.EscapeDataString(runtimeToken.Trim())}";
    }

    public static string BuildPublishedUrl(string server, string slug)
    {
        var appsBaseUrl = new Uri(ResolveAppsBaseUrl(server));
        var host = appsBaseUrl.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
            ? appsBaseUrl.Host["apps.".Length..]
            : appsBaseUrl.Host;
        var builder = new UriBuilder(appsBaseUrl)
        {
            Host = $"{slug.Trim()}.apps.{host}",
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

    public static string BuildStudioAppUrl(string? server, string? appId)
    {
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        return $"{ResolveStudioBaseUrl(server).TrimEnd('/')}/apps/{Uri.EscapeDataString(appId.Trim())}";
    }

    public static string BuildStudioApplicationUrl(string? server, string? appId)
    {
        var studioAppUrl = BuildStudioAppUrl(server, appId);
        return string.IsNullOrWhiteSpace(studioAppUrl)
            ? string.Empty
            : $"{studioAppUrl.TrimEnd('/')}/application";
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

    private static string ResolveStudioBaseUrl(string server)
    {
        var configuredStudioBaseUrl = Environment.GetEnvironmentVariable("AUTOMATON_STUDIO_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configuredStudioBaseUrl))
        {
            return configuredStudioBaseUrl.TrimEnd('/');
        }

        if (Uri.TryCreate(server, UriKind.Absolute, out var serverUri) &&
            serverUri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(serverUri)
            {
                Host = "studio." + serverUri.Host["api.".Length..],
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        if (Uri.TryCreate(server, UriKind.Absolute, out serverUri) && IsLocalDevelopmentApiHost(serverUri))
        {
            var builder = new UriBuilder(serverUri)
            {
                Port = 5173,
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return server.TrimEnd('/');
    }

    private static bool IsLocalDevelopmentApiHost(Uri serverUri)
    {
        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return serverUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || serverUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
}
