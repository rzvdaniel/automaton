using System.Collections.Concurrent;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// In-memory storage for app preview content.
/// Stores temporary HTML/CSS/JS files for live preview in the editor.
/// Content expires after 30 minutes of inactivity to prevent memory bloat.
/// </summary>
public class AppPreviewStorageService
{
    private readonly ConcurrentDictionary<string, PreviewContent> storage = new();
    private readonly Serilog.ILogger logger;
    private readonly Timer cleanupTimer;

    public AppPreviewStorageService()
    {
        logger = Log.ForContext<AppPreviewStorageService>();
        
        // Run cleanup every 5 minutes
        cleanupTimer = new Timer(CleanupExpiredContent, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        logger.Debug("App preview storage service initialized");
    }

    /// <summary>
    /// Store preview content for an app
    /// </summary>
    public void StorePreviewContent(Guid userId, Guid appId, string html, IDictionary<string, string>? files = null)
    {
        var key = GetKey(userId, appId);
        var normalizedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (files != null)
        {
            foreach (var entry in files)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    var normalizedKey = entry.Key.Replace('\\', '/').TrimStart('/');
                    normalizedFiles[normalizedKey] = entry.Value ?? string.Empty;
                }
            }
        }

        if (normalizedFiles.Count == 0)
        {
            normalizedFiles = ExtractFilesFromHtml(html);
        }

        var content = new PreviewContent
        {
            Html = html,
            LastAccessed = DateTime.UtcNow,
            Files = normalizedFiles
        };

        storage[key] = content;
        
        logger.Debug("Stored preview content for app {AppId} (user {UserId})", appId, userId);
    }

    /// <summary>
    /// Get preview content for an app
    /// </summary>
    public string? GetPreviewFile(Guid userId, Guid appId, string path)
    {
        var key = GetKey(userId, appId);

        if (!storage.TryGetValue(key, out var content))
        {
            return null;
        }

        // Update last accessed time
        content.LastAccessed = DateTime.UtcNow;

        // Normalize path (default to index.html)
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            path = "index.html";
        }

        // Remove leading slash
        path = path.Replace('\\', '/').TrimStart('/');

        // Check if it's the main HTML file
        if (path == "index.html")
        {
            return content.Html;
        }

        // Check extracted files (CSS, JS, images, etc.)
        if (content.Files.TryGetValue(path, out var fileContent))
        {
            return fileContent;
        }

        logger.Warning("Preview file not found: {Path} for app {AppId}", path, appId);
        return null;
    }

    /// <summary>
    /// Check if preview content exists for an app
    /// </summary>
    public bool HasPreview(Guid userId, Guid appId)
    {
        var key = GetKey(userId, appId);
        return storage.ContainsKey(key);
    }

    /// <summary>
    /// Remove preview content for an app
    /// </summary>
    public void ClearPreview(Guid userId, Guid appId)
    {
        var key = GetKey(userId, appId);
        storage.TryRemove(key, out _);
        logger.Debug("Cleared preview content for app {AppId}", appId);
    }

    /// <summary>
    /// Extract file references from HTML and parse inline content
    /// This handles multi-file apps where CSS/JS might be in separate files
    /// </summary>
    private Dictionary<string, string> ExtractFilesFromHtml(string html)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Extract inline CSS from <style> tags
            var styleMatches = System.Text.RegularExpressions.Regex.Matches(
                html,
                @"<style[^>]*>(.*?)</style>",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            foreach (System.Text.RegularExpressions.Match match in styleMatches)
            {
                if (match.Groups.Count > 1)
                {
                    var cssContent = match.Groups[1].Value;
                    var filename = $"inline-{files.Count}.css";
                    files[filename] = cssContent;
                }
            }

            // Extract inline JS from <script> tags (non-src)
            var scriptMatches = System.Text.RegularExpressions.Regex.Matches(
                html,
                @"<script(?![^>]*\bsrc\s*=)[^>]*>(.*?)</script>",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            foreach (System.Text.RegularExpressions.Match match in scriptMatches)
            {
                if (match.Groups.Count > 1)
                {
                    var jsContent = match.Groups[1].Value;
                    var filename = $"inline-{files.Count}.js";
                    files[filename] = jsContent;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to extract files from HTML during preview storage");
        }

        return files;
    }

    /// <summary>
    /// Cleanup expired preview content (older than 30 minutes)
    /// </summary>
    private void CleanupExpiredContent(object? state)
    {
        try
        {
            var expirationTime = DateTime.UtcNow.AddMinutes(-30);
            var expiredKeys = storage
                .Where(kvp => kvp.Value.LastAccessed < expirationTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                storage.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                logger.Debug("Cleaned up {Count} expired preview content entries", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during preview content cleanup");
        }
    }

    private static string GetKey(Guid userId, Guid appId)
    {
        return $"{userId}:{appId}";
    }

    private class PreviewContent
    {
        public required string Html { get; set; }
        public DateTime LastAccessed { get; set; }
        public Dictionary<string, string> Files { get; set; } = new();
    }
}
