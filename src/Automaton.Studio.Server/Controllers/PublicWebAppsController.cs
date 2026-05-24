using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for serving published web apps.
/// Apps are served from the frontend live file system (AppData/apps/{ownerId}/{appId}/live/frontend/).
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
    private readonly WebAppConfig _webAppConfig;

    public PublicWebAppsController(
        ApplicationDbContext context,
        AppFileStorageService fileStorageService,
        AppsService appsService,
        ILogger<PublicWebAppsController> logger,
        IOptions<WebAppConfig> webAppConfig)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _appsService = appsService;
        _logger = logger;
        _webAppConfig = webAppConfig.Value;
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
                .Where(w => w.IsPublished && w.MarketplaceStatus == MarketplaceAppStatuses.Approved && w.PublicUrl == slug)
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

            if (!app.OwnerId.HasValue)
            {
                _logger.LogWarning("App '{AppId}' has no owner", app.Id);
                return NotFound(new { message = "App configuration error" });
            }

            var htmlContent = await _fileStorageService.ReadRuntimeFileAsync(app.OwnerId.Value, app.Id, "index.html");
            if (htmlContent == null)
            {
                _logger.LogWarning("Published app '{AppId}' has no live/frontend/index.html", app.Id);
                return NotFound(new { message = "Runtime entry file not found" });
            }

            WebAppCachePolicy.ApplyHtmlDocumentHeaders(Response);

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
                .Where(w => w.IsPublished && w.MarketplaceStatus == MarketplaceAppStatuses.Approved && w.PublicUrl == slug)
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

            if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(path))
            {
                return NotFound(new { message = "File not found" });
            }

            var content = await _fileStorageService.ReadRuntimeFileBytesAsync(app.OwnerId.Value, app.Id, path);
            if (content == null)
            {
                return NotFound(new { message = "File not found" });
            }

            var contentType = WebAppRuntimeFilePolicy.GetContentType(path);

            WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
            WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, path, _webAppConfig);

            return File(content, contentType);
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

}
