namespace Automaton.Studio.Server.Services;

public static class WebAppRuntimeFilePolicy
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".css", ".js", ".mjs", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp",
        ".ico", ".txt", ".xml", ".webmanifest",
        ".wasm", ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".webm", ".pdf"
    };

    public static bool IsAllowedPublicPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);
    }

    public static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".wasm" => "application/wasm",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            ".otf" => "font/otf",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }
}
