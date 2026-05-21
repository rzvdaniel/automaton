using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for serving user-generated web apps.
/// Designed to be served from a separate subdomain (e.g., apps.automaton.monster)
/// for security isolation from the main application.
///
/// Routes:
/// - GET /{slug}           → Serves index.html for the app
/// - GET /{slug}/{*path}   → Serves any file from the app
///
/// Security:
/// - Served from separate domain to prevent XSS access to main app cookies
/// - Files validated against extension whitelist
/// - Path traversal protection
/// </summary>
[ApiController]
[Route("/")]
[AllowAnonymous]
public class UserAppsController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly ILogger<UserAppsController> logger;
    private readonly IWebHostEnvironment environment;
    private readonly WebAppConfig webAppConfig;

    public UserAppsController(
        ApplicationDbContext dbContext,
        AppFileStorageService fileStorageService,
        ILogger<UserAppsController> logger,
        IWebHostEnvironment environment,
        IOptions<WebAppConfig> webAppConfig)
    {
        this.dbContext = dbContext;
        this.fileStorageService = fileStorageService;
        this.logger = logger;
        this.environment = environment;
        this.webAppConfig = webAppConfig.Value;
    }

    /// <summary>
    /// Serve a user app's index.html by its public URL slug.
    /// Example: https://apps.automaton.monster/my-cool-app
    /// </summary>
    [HttpGet("apps/{slug}")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<IActionResult> ServeApp(string slug)
    {
        return await ServeAppFile(slug, "index.html");
    }

    /// <summary>
    /// Serve any file from a user app.
    /// Example: https://apps.automaton.monster/my-cool-app/styles.css
    /// </summary>
    [HttpGet("apps/{slug}/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeAppFile(string slug, string path = "index.html")
    {
        try
        {
            // Find the published app by slug
            var app = await dbContext.WebApps
                .Include(a => a.WebAppUsers)
                .FirstOrDefaultAsync(a =>
                    a.IsPublished &&
                    a.MarketplaceStatus == MarketplaceAppStatuses.Approved &&
                    a.PublicUrl != null &&
                    a.PublicUrl.ToLower() == slug.ToLower());

            if (app == null)
            {
                logger.LogWarning("App not found or not published: {Slug}", slug);
                return NotFound(GetErrorPage("App Not Found", "This app does not exist or is not published."));
            }

            // Check authentication if required.
            // Only gate index.html — static assets (JS, CSS, images) must load freely so the page can render.
            // Protecting individual asset files is pointless: they are useless without a working session.
            var isHtmlEntryPoint = string.IsNullOrEmpty(path) || path == "index.html" ||
                                   Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase);
            if (app.RequiresAuthentication && isHtmlEntryPoint)
            {
                var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
                if (!isAuthenticated)
                {
                    logger.LogWarning("Unauthenticated access to protected app: {Slug}", slug);
                    return Unauthorized(GetErrorPage("Authentication Required", "This app requires you to be logged in."));
                }
            }

            // Get the owner ID
            var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId;
            if (ownerId == null)
            {
                logger.LogError("App has no owner: {AppId}", app.Id);
                return StatusCode(500, GetErrorPage("Configuration Error", "This app is not properly configured."));
            }

            path = string.IsNullOrEmpty(path) ? "index.html" : path;

            // Extensionless public app routes are SPA/document routes, not file paths.
            var runtimePath = Path.GetExtension(path).Length == 0 ? "index.html" : path;
            if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(runtimePath))
            {
                return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
            }

            var content = await fileStorageService.ReadRuntimeFileBytesAsync(ownerId.Value, app.Id, runtimePath);
            if (content == null)
            {
                return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
            }

            var contentType = WebAppRuntimeFilePolicy.GetContentType(runtimePath);

            // Smart caching: HTML always revalidates, fingerprinted assets can be immutable,
            // plain app assets stay fresh without forcing users to add manual cache busting.
            WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, runtimePath, webAppConfig);

            return File(content, contentType);
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Path traversal attempt for app: {Slug}, path: {Path}", slug, path);
            return BadRequest(GetErrorPage("Access Denied", "Invalid file path."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving app file: {Slug}/{Path}", slug, path);
            return StatusCode(500, GetErrorPage("Error", "An error occurred while loading this app."));
        }
    }

    /// <summary>
    /// Generate a simple error page
    /// </summary>
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
}
