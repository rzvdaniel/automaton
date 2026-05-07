using Automaton.Studio.Server.Config;
using Microsoft.AspNetCore.Http;

namespace Automaton.Studio.Server.Services;

public static class WebAppCachePolicy
{
    private static readonly string[] VersionQueryKeys = ["v", "ver", "version", "hash"];

    public static void ApplyPublishedFileHeaders(HttpResponse response, HttpRequest request, string? path, WebAppConfig config)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = GetPublishedFileCacheControl(request, path, config);
    }

    public static void ApplyHtmlDocumentHeaders(HttpResponse response)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "public, max-age=0, must-revalidate";
    }

    private static string GetPublishedFileCacheControl(HttpRequest request, string? path, WebAppConfig config)
    {
        if (IsHtmlEntryPoint(path))
        {
            return "public, max-age=0, must-revalidate";
        }

        if (IsVersionedAsset(request, path))
        {
            return $"public, max-age={config.CacheMaxAgeSeconds}, immutable";
        }

        return "public, max-age=0, must-revalidate";
    }

    private static bool IsHtmlEntryPoint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return true;
        }

        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionedAsset(HttpRequest request, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsHtmlEntryPoint(path))
        {
            return false;
        }

        foreach (var key in VersionQueryKeys)
        {
            if (request.Query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return false;
        }

        var segments = fileNameWithoutExtension.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(IsLikelyVersionToken);
    }

    private static bool IsLikelyVersionToken(string segment)
    {
        if (segment.Length < 8)
        {
            return false;
        }

        var hasDigit = false;
        foreach (var character in segment)
        {
            if (!char.IsLetterOrDigit(character))
            {
                return false;
            }

            if (char.IsDigit(character))
            {
                hasDigit = true;
            }
        }

        return hasDigit;
    }
}