namespace Automaton.Core.Models;

public static class AppMediaPathHelper
{
    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Media path is required.");
        }

        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized) || normalized.Contains(':'))
        {
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Media path is required.");
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new UnauthorizedAccessException("Media path must stay inside the app media directory.");
            }
        }

        return string.Join('/', segments);
    }

    public static string ResolvePath(string mediaRoot, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullRoot = Path.GetFullPath(mediaRoot);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Media path must stay inside the app media directory.");
        }

        return fullPath;
    }

    public static string ToPublicPath(string relativePath) => $"/media/{NormalizeRelativePath(relativePath)}";
}
