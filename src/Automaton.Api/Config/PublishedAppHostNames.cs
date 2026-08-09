namespace Automaton.Api.Config;

public static class PublishedAppHostNames
{
    public static bool IsPrivateAppHost(string host, string appsDomain)
        => TryGetLabel(host, appsDomain, out var label)
            && IsPrivateAppLabel(label);

    public static bool IsPrivateAppLabel(string label)
        => label.Length == 36 &&
            label.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            label[4..].All(IsHexDigit);

    public static bool IsValidPublishedSlug(string slug)
        => !string.IsNullOrWhiteSpace(slug) &&
            slug.Length <= 63 &&
            slug[0] != '-' &&
            slug[^1] != '-' &&
            !IsPrivateAppLabel(slug) &&
            slug.All(character =>
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character == '-');

    public static bool TryGetPublishedAppSlug(string host, string appsDomain, out string slug)
    {
        slug = string.Empty;
        if (!TryGetLabel(host, appsDomain, out var label) || IsPrivateAppLabel(label))
        {
            return false;
        }

        if (!IsValidPublishedSlug(label))
        {
            return false;
        }

        slug = label;
        return true;
    }

    public static string BuildPublishedAppHost(string slug, string appsDomain)
        => $"{slug}.{appsDomain}";

    public static string BuildPrivateAppHost(Guid appId, string appsDomain)
        => $"app-{appId:N}.{appsDomain}";

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

        label = prefix.ToLowerInvariant();
        return true;
    }

    private static bool IsHexDigit(char character)
        => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
}
