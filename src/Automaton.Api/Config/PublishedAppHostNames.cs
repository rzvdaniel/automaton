using System.Text.RegularExpressions;

namespace Automaton.Api.Config;

public static partial class PublishedAppHostNames
{
    public static bool IsPrivateAppHost(string host, string appsDomain)
        => TryGetLabel(host, appsDomain, out var label)
            && PrivateLabelRegex().IsMatch(label);

    public static bool TryGetPublishedAppSlug(string host, string appsDomain, out string slug)
    {
        slug = string.Empty;
        if (!TryGetLabel(host, appsDomain, out var label) || PrivateLabelRegex().IsMatch(label))
        {
            return false;
        }

        if (!SlugRegex().IsMatch(label))
        {
            return false;
        }

        slug = label;
        return true;
    }

    public static string BuildPublishedAppHost(string slug, string appsDomain)
        => $"{slug}.{appsDomain}";

    private static bool TryGetLabel(string host, string appsDomain, out string label)
    {
        label = string.Empty;
        var normalizedHost = host.TrimEnd('.');
        var normalizedDomain = appsDomain.Trim().TrimEnd('.');
        var suffix = $".{normalizedDomain}";
        if (string.IsNullOrWhiteSpace(normalizedDomain) ||
            !normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = normalizedHost[..^suffix.Length];
        if (prefix.Length == 0 || prefix.Contains('.'))
        {
            return false;
        }

        label = prefix;
        return true;
    }

    [GeneratedRegex("^app-[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateLabelRegex();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
