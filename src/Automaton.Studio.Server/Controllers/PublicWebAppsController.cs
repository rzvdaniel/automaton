using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for serving published web apps.
/// Apps are served from the file system (AppData/apps/{ownerId}/{appId}/).
///
/// Routes:
/// - GET /api/web/{slug}              → Returns app metadata + index.html content
/// - GET /api/web/{slug}/files/{path} → Returns any file from the app
/// </summary>
[ApiController]
[Route("api/web")]
[AllowAnonymous]
public class PublicWebAppsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AppFileStorageService _fileStorageService;
    private readonly AppsService _appsService;
    private readonly ILogger<PublicWebAppsController> _logger;

    public PublicWebAppsController(
        ApplicationDbContext context,
        AppFileStorageService fileStorageService,
        AppsService appsService,
        ILogger<PublicWebAppsController> logger)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _appsService = appsService;
        _logger = logger;
    }

    /// <summary>
    /// Get published web app by slug (public access, authentication checked client-side)
    /// Now reads HTML content from file system instead of database.
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublishedApp(string slug)
    {
        try
        {
            // Query published app by slug with owner info
            var app = await _context.WebApps
                .Include(w => w.WebAppUsers)
                .Where(w => w.IsPublished && w.PublicUrl == slug)
                .Select(w => new
                {
                    w.Id,
                    w.Name,
                    w.Summary,
                    w.Description,
                    w.PublishedAt,
                    w.RequiresAuthentication,
                    OwnerId = w.WebAppUsers.FirstOrDefault() != null ? w.WebAppUsers.FirstOrDefault()!.UserId : (Guid?)null
                })
                .FirstOrDefaultAsync();

            if (app == null)
            {
                _logger.LogWarning("Published app with slug '{Slug}' not found", slug);
                return NotFound(new { message = "App not found or not published" });
            }

            // Log access attempt with authentication status
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

            if (app.RequiresAuthentication)
            {
                if (isAuthenticated)
                {
                    _logger.LogInformation("Authenticated user '{User}' accessing protected app '{AppName}' (slug: {Slug})",
                        User.Identity?.Name, app.Name, slug);
                }
                else
                {
                    _logger.LogWarning("Unauthenticated access attempt to protected app '{AppName}' (slug: {Slug})", app.Name, slug);
                    // Don't return 401 - let client handle authentication check
                }
            }
            else
            {
                _logger.LogInformation("Public access to app '{AppName}' (slug: {Slug}) - Auth: {IsAuth}",
                    app.Name, slug, isAuthenticated);
            }

            // Increment view count
            try
            {
                await _appsService.IncrementViewCountAsync(app.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to increment view count for app {AppId}", app.Id);
            }

            // Read index.html — priority: dist/ (Vite/webpack builds) → frontend/ (AI-generated) → root (legacy)
            string htmlContent;
            if (app.OwnerId.HasValue)
            {
                htmlContent = await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, "dist/index.html")
                           ?? await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, "frontend/index.html")
                           ?? await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, "index.html")
                           ?? GetDefaultHtmlContent();
            }
            else
            {
                _logger.LogWarning("App '{AppId}' has no owner, using default content", app.Id);
                htmlContent = GetDefaultHtmlContent();
            }

            return Ok(new
            {
                app.Id,
                app.Name,
                app.Summary,
                app.Description,
                HtmlContent = htmlContent,
                app.PublishedAt,
                app.RequiresAuthentication,
                IsUserAuthenticated = isAuthenticated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving published app with slug '{Slug}'", slug);
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Get a specific file from a published app.
    /// Allows multi-file apps to load CSS, JS, images, etc.
    /// Example: GET /api/web/my-app/files/styles.css
    /// </summary>
    [HttpGet("{slug}/files/{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublishedAppFile(string slug, string path)
    {
        try
        {
            // Query published app by slug with owner info
            var app = await _context.WebApps
                .Include(w => w.WebAppUsers)
                .Where(w => w.IsPublished && w.PublicUrl == slug)
                .Select(w => new
                {
                    w.Id,
                    w.Name,
                    w.RequiresAuthentication,
                    OwnerId = w.WebAppUsers.FirstOrDefault() != null ? w.WebAppUsers.FirstOrDefault()!.UserId : (Guid?)null
                })
                .FirstOrDefaultAsync();

            if (app == null)
            {
                return NotFound(new { message = "App not found or not published" });
            }

            // Only gate HTML entry points on authentication — static assets (JS, CSS, images) must load freely.
            var isHtmlEntryPoint = string.IsNullOrEmpty(path) || path == "index.html" ||
                                   Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase);
            if (app.RequiresAuthentication && isHtmlEntryPoint)
            {
                var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
                if (!isAuthenticated)
                {
                    return Unauthorized(new { message = "Authentication required" });
                }
            }

            if (!app.OwnerId.HasValue)
            {
                return NotFound(new { message = "App configuration error" });
            }

            // Read file — priority: dist/ (Vite/webpack builds) → frontend/ (AI-generated) → root (legacy)
            var content = await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, "dist/" + path)
                       ?? await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, "frontend/" + path)
                       ?? await _fileStorageService.ReadAppFileAsync(app.OwnerId.Value, app.Id, path);
            if (content == null)
            {
                return NotFound(new { message = "File not found" });
            }

            // Return with appropriate content type
            var contentType = GetContentType(path);

            // Add cache headers
            Response.Headers.CacheControl = "public, max-age=3600";
            Response.Headers.XContentTypeOptions = "nosniff";

            return Content(content, contentType);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Path traversal attempt for app slug '{Slug}', path '{Path}'", slug, path);
            return BadRequest(new { message = "Invalid file path" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file '{Path}' for app slug '{Slug}'", path, slug);
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Get the MIME content type for a file based on extension
    /// </summary>
    private static string GetContentType(string path)
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
            ".md" => "text/markdown; charset=utf-8",
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

    private static string GetDefaultHtmlContent()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>App</title>
</head>
<body>
    <h1>App Content Not Available</h1>
    <p>This app has no content defined.</p>
</body>
</html>";
    }
}
