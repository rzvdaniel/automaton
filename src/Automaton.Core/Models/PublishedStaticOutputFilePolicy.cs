namespace Automaton.Core.Models;

/// <summary>Allows only portable public assets from an explicitly selected static output tree.</summary>
public static class PublishedStaticOutputFilePolicy
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".css", ".js", ".mjs", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".txt", ".xml", ".webmanifest",
        ".wasm", ".woff", ".woff2", ".ttf", ".eot", ".otf", ".mp3", ".mp4", ".webm", ".pdf"
    };

    private static readonly HashSet<string> PrivateSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "source", "secrets", "secret", "credentials", "private", "logs", "log", "data", "cache",
        "actions", "workflows", "node_modules", ".automaton", ".git"
    };

    public static bool IsAllowedFilePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized[0] == '/'
            || normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            return false;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && segments.All(segment => segment is not ("." or "..")
                && !segment.StartsWith(".", StringComparison.Ordinal)
                && !IsPrivateSegment(segment))
            && AllowedExtensions.Contains(Path.GetExtension(normalized));
    }

    private static bool IsPrivateSegment(string segment)
    {
        var stem = Path.GetFileNameWithoutExtension(segment);
        return PrivateSegments.Contains(segment)
            || PrivateSegments.Contains(stem)
            || stem.StartsWith("secret", StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith("credential", StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith("private", StringComparison.OrdinalIgnoreCase);
    }
}
