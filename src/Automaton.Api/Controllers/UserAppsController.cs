using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Attributes;

namespace Automaton.Api.Controllers;

/// <summary>
/// Controller for serving user-generated web apps.
/// Designed to be served from a separate subdomain (e.g., apps.automaton.monster)
/// for security isolation from the main application.
///
/// Routes:
/// - GET /{slug}               → Serves index.html for the dedicated apps host
/// - GET /{slug}/{*path}       → Serves any file from the dedicated apps host
/// - GET /apps/{slug}          → Serves index.html for Studio/local hosts
/// - GET /apps/{slug}/{*path}  → Serves any file for Studio/local hosts
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
    private readonly AppsService appsService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AppMediaStorageService mediaStorageService;
    private readonly PublishedAppStorageService publishedAppStorageService;
    private readonly AppsControllerExecutionService appsControllerExecutionService;
    private readonly ILogger<UserAppsController> logger;
    private readonly IWebHostEnvironment environment;
    private readonly WebAppConfig webAppConfig;
    private readonly PrivateAppLaunchService privateAppLaunchService;

    public UserAppsController(
        ApplicationDbContext dbContext,
        AppsService appsService,
        AppFileStorageService fileStorageService,
        AppMediaStorageService mediaStorageService,
        PublishedAppStorageService publishedAppStorageService,
        AppsControllerExecutionService appsControllerExecutionService,
        ILogger<UserAppsController> logger,
        IWebHostEnvironment environment,
        IOptions<WebAppConfig> webAppConfig,
        PrivateAppLaunchService privateAppLaunchService)
    {
        this.dbContext = dbContext;
        this.appsService = appsService;
        this.fileStorageService = fileStorageService;
        this.mediaStorageService = mediaStorageService;
        this.publishedAppStorageService = publishedAppStorageService;
        this.appsControllerExecutionService = appsControllerExecutionService;
        this.logger = logger;
        this.environment = environment;
        this.webAppConfig = webAppConfig.Value;
        this.privateAppLaunchService = privateAppLaunchService;
    }

    /// <summary>
    /// Serve a user app's index.html by its public URL slug.
    /// Example: https://apps.automaton.monster/my-cool-app
    /// </summary>
    [HttpGet("a/{appId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeRestrictedApp(Guid appId)
    {
        return await ServeRestrictedAppFile(appId, "index.html");
    }

    [HttpGet("a/{appId:guid}/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeRestrictedAppFile(Guid appId, string path = "index.html")
    {
        try
        {
            var isHtmlEntryPoint =
                string.IsNullOrWhiteSpace(path) ||
                string.Equals(path, "index.html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase);

            var app = isHtmlEntryPoint
                ? await GetAuthorizedRestrictedAppAsync(appId)
                : await GetRuntimeAppAsync(appId);

            if (app == null)
            {
                return HtmlPage(
                    StatusCodes.Status401Unauthorized,
                    "Authentication Required",
                    "This restricted app requires a valid launch or login.");
            }

            if (app.OwnerId == null)
            {
                return HtmlPage(
                    StatusCodes.Status500InternalServerError,
                    "Configuration Error",
                    "This app is not properly configured.");
            }

            return await ServeRuntimeFileAsync(
                app.Id,
                app.OwnerId.Value,
                path,
                backendApiPath: $"/a/{app.Id}/api",
                useCurrentHostApiBase: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving restricted app file: {AppId}/{Path}", appId, path);
            return HtmlPage(
                StatusCodes.Status500InternalServerError,
                "Error",
                "An error occurred while loading this app.");
        }
    }

    private async Task<PublishedAppInfo?> GetAuthorizedRestrictedAppAsync(Guid appId)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId))
        {
            return await appsService.GetAccessibleAppInfoByIdAsync(appId);
        }

        return await appsService.GetAccessibleAppInfoByIdAsync(appId);
    }

    private async Task<PublishedAppInfo?> GetRuntimeAppAsync(Guid appId)
    {
        var app = await dbContext.WebApps
            .Include(a => a.WebAppUsers)
            .Where(a => a.Id == appId && a.AccessStatus == AppAccessStatuses.Active)
            .Select(a => new PublishedAppInfo
            {
                Id = a.Id,
                Name = a.Name,
                PublicUrl = a.PublicUrl,
                OwnerId = a.WebAppUsers
                    .Select(u => (Guid?)u.UserId)
                    .FirstOrDefault(),
                RequiresAuthentication = a.RequiresAuthentication,
                ExecutionRunnerId = a.DeploymentTargets
                    .Where(target => target.IsDefault)
                    .Select(target => target.ExecutionRunnerId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return app;
    }

    /// <summary>
    /// Serve any file from a user app.
    /// Example: https://apps.automaton.monster/my-cool-app/styles.css
    /// </summary>
    [HttpGet("apps/{slug}/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeStudioHostedAppFile(string slug, string path = "index.html")
    {
        return await ServePublishedAppFile(slug, path);
    }

    [HttpGet("apps/{slug}/media/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeStudioHostedAppMedia(string slug, string path, CancellationToken cancellationToken)
    {
        return await ServePublishedAppMedia(slug, path, cancellationToken);
    }

    [HttpGet("{slug}/{*path}")]
    [AppsHostOnly]
    [AllowAnonymous]
    public async Task<IActionResult> ServeDedicatedAppsHostAppFile(string slug, string path = "index.html")
    {
        return await ServePublishedAppFile(slug, path);
    }

    [HttpGet("{slug}/media/{*path}")]
    [AppsHostOnly]
    [AllowAnonymous]
    public async Task<IActionResult> ServeDedicatedAppsHostAppMedia(string slug, string path, CancellationToken cancellationToken)
    {
        return await ServePublishedAppMedia(slug, path, cancellationToken);
    }

    private async Task<IActionResult> ServePublishedAppFile(string slug, string path = "index.html")
    {
        try
        {
            var app = await dbContext.WebApps
                .Include(a => a.WebAppUsers)
                .FirstOrDefaultAsync(a =>
                    a.IsPublished &&
                    a.AccessStatus == AppAccessStatuses.Active &&
                    a.PublicUrl != null &&
                    a.PublicUrl.ToLower() == slug.ToLower());

            if (app == null)
            {
                logger.LogWarning("App not found or not published: {Slug}", slug);
                return HtmlPage(
                    StatusCodes.Status404NotFound,
                    "App Not Found",
                    "This app does not exist or is not published.");
            }

            var isHtmlEntryPoint =
                string.IsNullOrWhiteSpace(path) ||
                string.Equals(path, "index.html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase);

            if (app.RequiresAuthentication && isHtmlEntryPoint && User.Identity?.IsAuthenticated != true)
            {
                logger.LogWarning("Unauthenticated access to protected app: {Slug}", slug);
                return HtmlPage(
                    StatusCodes.Status401Unauthorized,
                    "Authentication Required",
                    "This app requires you to be logged in.");
            }

            var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId;
            if (ownerId == null)
            {
                logger.LogError("App has no owner: {AppId}", app.Id);
                return HtmlPage(
                    StatusCodes.Status500InternalServerError,
                    "Configuration Error",
                    "This app is not properly configured.");
            }

            logger.LogDebug(
                "Published app launch tracked: slug={Slug}, path={Path}, appId={AppId}, htmlEntry={HtmlEntry}",
                slug, path, app.Id, isHtmlEntryPoint);

            return await ServePublishedStaticFileAsync(
                app.Id,
                slug,
                path,
                backendApiPath: $"/api/web-apps/{Uri.EscapeDataString(slug)}");
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Path traversal attempt for app: {Slug}, path: {Path}", slug, path);
            return HtmlPage(
                StatusCodes.Status400BadRequest,
                "Access Denied",
                "Invalid file path.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving app file: {Slug}/{Path}", slug, path);
            return HtmlPage(
                StatusCodes.Status500InternalServerError,
                "Error",
                "An error occurred while loading this app.");
        }
    }

    private async Task<IActionResult> ServePublishedAppMedia(string slug, string path, CancellationToken cancellationToken)
    {
        var app = await appsService.GetPublishedAppInfoAsync(slug, cancellationToken);
        if (app == null)
        {
            return NotFound();
        }

        if (app.RequiresAuthentication && User.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning("Unauthenticated access to protected app media: {Slug}", slug);
            return Unauthorized();
        }

        if (app.OwnerId == null)
        {
            return NotFound();
        }

        var statusCode = await mediaStorageService.ProxyReadAsync(app.Id, app.OwnerId.Value, path, Response, cancellationToken);
        if (statusCode == StatusCodes.Status200OK)
        {
            return new EmptyResult();
        }

        return StatusCode(statusCode);
    }

    private async Task<IActionResult> ServeRuntimeFileAsync(Guid appId, Guid ownerId, string? path, string backendApiPath, bool useCurrentHostApiBase = false)
    {
        path = string.IsNullOrEmpty(path) ? "index.html" : path;

        var runtimePath = Path.GetExtension(path).Length == 0 ? "index.html" : path;
        if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(runtimePath))
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var runnerContent = await appsControllerExecutionService.ReadRuntimeFrontendFileFromRunnerAsync(
            appId,
            ownerId,
            runtimePath,
            HttpContext.RequestAborted);
        var content = runnerContent?.Content;
        if (content == null)
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var contentType = runnerContent?.ContentType ?? WebAppRuntimeFilePolicy.GetContentType(runtimePath);
        WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
        WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, runtimePath, webAppConfig);

        if (contentType == "text/html")
        {
            var html = Encoding.UTF8.GetString(content);
            var injectedHtml = InjectRuntimeScripts(html, appId, backendApiPath, useCurrentHostApiBase);
            return Content(injectedHtml, "text/html; charset=utf-8", Encoding.UTF8);
        }

        return File(content, contentType);
    }

    private async Task<IActionResult> ServePublishedStaticFileAsync(Guid appId, string slug, string? path, string backendApiPath)
    {
        path = string.IsNullOrEmpty(path) ? "index.html" : path;

        var publishedPath = Path.GetExtension(path).Length == 0 ? "index.html" : path;
        if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(publishedPath))
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var content = await publishedAppStorageService.ReadPublishedFileBytesAsync(slug, publishedPath);
        if (content == null)
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var contentType = WebAppRuntimeFilePolicy.GetContentType(publishedPath);
        WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
        WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, publishedPath, webAppConfig);

        if (contentType == "text/html")
        {
            var html = Encoding.UTF8.GetString(content);
            var injectedHtml = InjectRuntimeScripts(html, appId, backendApiPath);
            return Content(injectedHtml, "text/html; charset=utf-8", Encoding.UTF8);
        }

        return File(content, contentType);
    }

    private string InjectRuntimeScripts(string html, Guid appId, string backendApiPath, bool useCurrentHostApiBase = false)
    {
        if (html.Contains("automaton-config.js", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var apiHost = useCurrentHostApiBase
            ? Request.Host.Host
            : Request.Host.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
                ? $"api.{Request.Host.Host["apps.".Length..]}"
                : Request.Host.Host;
        var apiBaseUrl = $"{Request.Scheme}://{apiHost}";
        if (Request.Host.Port.HasValue)
        {
            apiBaseUrl += $":{Request.Host.Port.Value}";
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            appId,
            apiBaseUrl,
            backendApiPath,
            runtimeToken = Request.Query["token"].FirstOrDefault()
        });

        var runtimeScripts = $"""
    <script>window.__AUTOMATON_APP__ = {payload};</script>
""";

        var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return headCloseIndex >= 0
            ? html.Insert(headCloseIndex, runtimeScripts)
            : runtimeScripts + html;
    }

    private static ContentResult HtmlPage(int statusCode, string title, string message)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "text/html; charset=utf-8",
            Content = GetErrorPage(title, message)
        };
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
