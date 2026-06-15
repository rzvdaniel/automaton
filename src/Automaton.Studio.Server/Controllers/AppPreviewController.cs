using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for app preview functionality.
/// Stores and serves temporary preview content for the app editor.
/// </summary>
[ApiController]
[Authorize]
public class AppPreviewController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppPreviewStorageService previewStorage;
    private readonly AppFileStorageService fileStorage;
    private readonly ILogger<AppPreviewController> logger;

    public AppPreviewController(
        ApplicationDbContext dbContext,
        AppPreviewStorageService previewStorage,
        AppFileStorageService fileStorage,
        ILogger<AppPreviewController> logger)
    {
        this.dbContext = dbContext;
        this.previewStorage = previewStorage;
        this.fileStorage = fileStorage;
        this.logger = logger;
    }

    /// <summary>
    /// Store preview content for an app.
    /// This is called by the frontend when the user is editing an app.
    /// </summary>
    [HttpPost("api/apps/{appId}/preview")]
    public async Task<IActionResult> StorePreviewContent(
        Guid appId,
        [FromBody] StorePreviewRequest request)
    {
        try
        {
            var userId = GetUserId();

            // Verify user has access to this app
            var hasAccess = await dbContext.WebAppUsers
                .AnyAsync(wu => wu.WebAppId == appId && wu.UserId == userId);

            if (!hasAccess)
            {
                // Check if it's a new app (appId might be temporary)
                // For new apps being created, we'll allow preview but won't persist
                logger.LogWarning("User {UserId} attempted to preview app {AppId} without access", userId, appId);
            }

            // Resolve the app owner for consistent file paths
            var ownerId = await dbContext.WebApps
                .Include(app => app.WebAppUsers)
                .Where(app => app.Id == appId)
                .Select(app => app.WebAppUsers.FirstOrDefault().UserId)
                .FirstOrDefaultAsync();

            var effectiveUserId = ownerId == Guid.Empty ? userId : ownerId;

            if (ownerId != Guid.Empty && ownerId != userId)
            {
                logger.LogDebug(
                    "Preview content stored under owner {OwnerId} for app {AppId} (requested by {UserId})",
                    ownerId,
                    appId,
                    userId);
            }

            // Store the HTML content in memory
            previewStorage.StorePreviewContent(effectiveUserId, appId, request.Html, request.Files);

            logger.LogDebug("Stored preview content for app {AppId} (user {UserId})", appId, effectiveUserId);

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store preview content for app {AppId}", appId);
            return StatusCode(500, new { error = "Failed to store preview content" });
        }
    }

    /// <summary>
    /// Serve preview files from memory.
    /// This mimics the published app endpoint but serves from in-memory storage.
    /// </summary>
    [HttpGet("app-preview/{userId}/{appId}/{*path}")]
    [AllowAnonymous] // Preview is accessible without auth (but requires knowing the GUID)
    public async Task<IActionResult> ServePreviewFile(Guid userId, Guid appId, string? path = null)
    {
        try
        {
            var ownerId = await dbContext.WebApps
                .Include(app => app.WebAppUsers)
                .Where(app => app.Id == appId)
                .Select(app => app.WebAppUsers.FirstOrDefault().UserId)
                .FirstOrDefaultAsync();

            var effectiveUserId = ownerId == Guid.Empty ? userId : ownerId;

            var requestedPath = path ?? "index.html";
            var filePath = string.IsNullOrEmpty(path) || path == "/" ? "index.html" : path.TrimStart('/');
            var contentType = GetContentType(requestedPath);

            if (IsBinaryPreviewFile(requestedPath))
            {
                var bytes = await ReadPreviewFileBytesAsync(effectiveUserId, userId, appId, filePath);
                if (bytes == null)
                {
                    logger.LogWarning("Preview binary file not found: {Path} for app {AppId}", path, appId);
                    return NotFound(GetErrorPage("File Not Found", $"The file '{path ?? "index.html"}' was not found in preview."));
                }

                SetNoCacheHeaders();
                Response.Headers.XContentTypeOptions = "nosniff";
                WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
                return File(bytes, contentType);
            }

            // First, try to get from memory (live preview)
            var content = previewStorage.GetPreviewFile(effectiveUserId, appId, requestedPath);

            if (content == null && effectiveUserId != userId)
            {
                content = previewStorage.GetPreviewFile(userId, appId, requestedPath);
            }

            // If not in memory, try loading from file storage (persisted content)
            if (content == null)
            {
                content = await fileStorage.ReadSourceFileAsync(effectiveUserId, appId, ToSourceFrontendPath(filePath));

                if (content == null && effectiveUserId != userId)
                {
                    content = await fileStorage.ReadSourceFileAsync(userId, appId, ToSourceFrontendPath(filePath));
                }
            }

            if (content == null)
            {
                logger.LogWarning("Preview file not found: {Path} for app {AppId}", path, appId);
                return NotFound(GetErrorPage("File Not Found", $"The file '{path ?? "index.html"}' was not found in preview."));
            }

            Response.Headers.XContentTypeOptions = "nosniff";
            SetNoCacheHeaders();
            WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);

            // Return with appropriate content type
            return Content(content, contentType, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving preview file: {AppId}/{Path}", appId, path);
            return StatusCode(500, GetErrorPage("Error", "An error occurred while loading the preview."));
        }
    }

    private async Task<byte[]?> ReadPreviewFileBytesAsync(Guid effectiveUserId, Guid requestUserId, Guid appId, string path)
    {
        var sourcePath = ToSourceFrontendPath(path);
        var bytes = await fileStorage.ReadSourceFileBytesAsync(effectiveUserId, appId, sourcePath);
        bytes ??= await fileStorage.ReadRuntimeFileBytesAsync(effectiveUserId, appId, path);

        if (bytes == null && effectiveUserId != requestUserId)
        {
            bytes = await fileStorage.ReadSourceFileBytesAsync(requestUserId, appId, sourcePath);
            bytes ??= await fileStorage.ReadRuntimeFileBytesAsync(requestUserId, appId, path);
        }

        return bytes;
    }

    private void SetNoCacheHeaders()
    {
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            ".otf" => "font/otf",
            ".map" => "application/json",
            _ => "application/octet-stream"
        };
    }

    private static bool IsBinaryPreviewFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".ico"
            or ".woff" or ".woff2" or ".ttf" or ".otf"
            or ".mp4" or ".webm" or ".mp3" or ".wav" or ".pdf";
    }

    private static string GetErrorPage(string title, string message)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            min-height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }}
        .container {{
            text-align: center;
            padding: 40px;
            background: rgba(255,255,255,0.1);
            border-radius: 10px;
            backdrop-filter: blur(10px);
        }}
        h1 {{ margin: 0 0 10px; }}
        p {{ margin: 0; opacity: 0.8; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>{title}</h1>
        <p>{message}</p>
    </div>
</body>
</html>";
    }

    public class StorePreviewRequest
    {
        public required string Html { get; set; }
        public Dictionary<string, string>? Files { get; set; }
    }

    private static string ToSourceFrontendPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"frontend/{normalized}";
    }
}
