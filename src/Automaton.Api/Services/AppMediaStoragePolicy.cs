using Microsoft.AspNetCore.Http;
using System.Text;

namespace Automaton.Api.Services;

public static class AppMediaStoragePolicy
{
    private static readonly Dictionary<string, string[]> AllowedMimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".gif"] = ["image/gif"],
        [".pdf"] = ["application/pdf"],
        [".txt"] = ["text/plain"]
    };

    public static bool TryValidateUpload(IFormFile? file, long maxUploadBytes, out string error, out string extension, out string contentType)
    {
        error = string.Empty;
        extension = string.Empty;
        contentType = "application/octet-stream";

        if (file is null || file.Length == 0)
        {
            error = "No file provided.";
            return false;
        }

        if (file.Length > maxUploadBytes)
        {
            error = $"File exceeds the {maxUploadBytes} byte upload limit.";
            return false;
        }

        extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedMimeTypesByExtension.TryGetValue(extension, out var allowedMimeTypes))
        {
            error = "Unsupported file type.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType)
            && !allowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            error = "Unsupported content type.";
            return false;
        }

        contentType = allowedMimeTypes[0];
        return true;
    }

    public static bool TryGetContentTypeForPath(string relativePath, out string contentType)
    {
        contentType = "application/octet-stream";
        var extension = Path.GetExtension(relativePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedMimeTypesByExtension.TryGetValue(extension, out var allowedMimeTypes))
        {
            return false;
        }

        contentType = allowedMimeTypes[0];
        return true;
    }

    public static string BuildStorageRelativePath(string originalFileName, DateTime utcNow)
    {
        var extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty);
        var slug = Slugify(baseName);
        var randomId = Guid.NewGuid().ToString("N")[..6];
        return $"uploads/{utcNow:yyyy}/{utcNow:MM}/{slug}-{randomId}{extension}";
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "file";
        }

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            builder.Append('-');
            lastWasDash = true;
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "file" : slug;
    }
}
