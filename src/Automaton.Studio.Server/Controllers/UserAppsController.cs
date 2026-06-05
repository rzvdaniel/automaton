using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

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
    private readonly AppsService appsService;
    private readonly AppFileStorageService fileStorageService;
    private readonly ILogger<UserAppsController> logger;
    private readonly IWebHostEnvironment environment;
    private readonly WebAppConfig webAppConfig;
    private readonly PrivateAppLaunchService privateAppLaunchService;

    public UserAppsController(
        ApplicationDbContext dbContext,
        AppsService appsService,
        AppFileStorageService fileStorageService,
        ILogger<UserAppsController> logger,
        IWebHostEnvironment environment,
        IOptions<WebAppConfig> webAppConfig,
        PrivateAppLaunchService privateAppLaunchService)
    {
        this.dbContext = dbContext;
        this.appsService = appsService;
        this.fileStorageService = fileStorageService;
        this.logger = logger;
        this.environment = environment;
        this.webAppConfig = webAppConfig.Value;
        this.privateAppLaunchService = privateAppLaunchService;
    }

    [HttpGet("runtime/automaton-api.js")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeRuntimeBridgeScript(CancellationToken cancellationToken)
    {
        var scriptPath = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "..",
            "docker",
            "nginx",
            "app-runtime",
            "automaton-api.js"));

        if (!System.IO.File.Exists(scriptPath))
        {
            return NotFound();
        }

        var script = await System.IO.File.ReadAllTextAsync(scriptPath, cancellationToken);
        return Content(script, "application/javascript; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>
    /// Serve a user app's index.html by its public URL slug.
    /// Example: https://apps.automaton.monster/my-cool-app
    /// </summary>
    [HttpGet("private/{appId:guid}")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<IActionResult> ServePrivateApp(Guid appId)
    {
        return await ServePrivateAppFile(appId, "index.html");
    }

    [HttpGet("private/{appId:guid}/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServePrivateAppFile(Guid appId, string path = "index.html")
    {
        try
        {
            var app = await GetAuthorizedPrivateAppAsync(appId);
            if (app == null)
            {
                return Unauthorized(GetErrorPage("Authentication Required", "This private app requires a valid launch or login."));
            }

            if (app.OwnerId == null)
            {
                return StatusCode(500, GetErrorPage("Configuration Error", "This app is not properly configured."));
            }

            return await ServeRuntimeFileAsync(
                app.Id,
                app.OwnerId.Value,
                path,
                backendApiPath: $"/api/web-apps/app/{app.Id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving private app file: {AppId}/{Path}", appId, path);
            return StatusCode(500, GetErrorPage("Error", "An error occurred while loading this app."));
        }
    }

    private async Task<Models.PublishedAppInfo?> GetAuthorizedPrivateAppAsync(Guid appId)
    {
        var launchCode = Request.Query["launch"].FirstOrDefault();
        if (privateAppLaunchService.TryConsumeLaunchCode(appId, launchCode, out var launchedUserId))
        {
            var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, launchedUserId);
            if (app == null)
            {
                return null;
            }

            var runtimeToken = privateAppLaunchService.CreateRuntimeToken(appId, launchedUserId);
            if (!string.IsNullOrWhiteSpace(runtimeToken))
            {
                Response.Cookies.Append(
                    PrivateAppAuthConstants.CookieName,
                    runtimeToken,
                    privateAppLaunchService.BuildRuntimeCookieOptions(Request));
            }

            return app;
        }

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

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ServeApp(string slug)
    {
        return await ServeAppFile(slug, "index.html");
    }

    /// <summary>
    /// Serve any file from a user app.
    /// Example: https://apps.automaton.monster/my-cool-app/styles.css
    /// </summary>
    [HttpGet("{slug}/{*path}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeAppFile(string slug, string path = "index.html")
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
                return NotFound(GetErrorPage("App Not Found", "This app does not exist or is not published."));
            }

            var isHtmlEntryPoint = string.IsNullOrEmpty(path) || path == "index.html" ||
                                   Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase);
            if (app.RequiresAuthentication && isHtmlEntryPoint && User.Identity?.IsAuthenticated != true)
            {
                logger.LogWarning("Unauthenticated access to protected app: {Slug}", slug);
                return Unauthorized(GetErrorPage("Authentication Required", "This app requires you to be logged in."));
            }

            var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId;
            if (ownerId == null)
            {
                logger.LogError("App has no owner: {AppId}", app.Id);
                return StatusCode(500, GetErrorPage("Configuration Error", "This app is not properly configured."));
            }

            return await ServeRuntimeFileAsync(
                app.Id,
                ownerId.Value,
                path,
                backendApiPath: $"/api/web-apps/{Uri.EscapeDataString(slug)}");
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

    private async Task<IActionResult> ServeRuntimeFileAsync(Guid appId, Guid ownerId, string? path, string backendApiPath)
    {
        path = string.IsNullOrEmpty(path) ? "index.html" : path;

        var runtimePath = Path.GetExtension(path).Length == 0 ? "index.html" : path;
        if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(runtimePath))
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var content = await fileStorageService.ReadRuntimeFileBytesAsync(ownerId, appId, runtimePath);
        if (content == null)
        {
            return NotFound(GetErrorPage("File Not Found", $"The file '{path}' was not found."));
        }

        var contentType = WebAppRuntimeFilePolicy.GetContentType(runtimePath);
        WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
        WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, runtimePath, webAppConfig);

        if (contentType == "text/html")
        {
            var html = Encoding.UTF8.GetString(content);
            var injectedHtml = InjectRuntimeScripts(html, backendApiPath);
            return Content(injectedHtml, "text/html; charset=utf-8", Encoding.UTF8);
        }

        return File(content, contentType);
    }

    private string InjectRuntimeScripts(string html, string backendApiPath)
    {
        if (html.Contains("automaton-config.js", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("automaton-api.js", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var apiHost = Request.Host.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
            ? $"api.{Request.Host.Host["apps.".Length..]}"
            : Request.Host.Host;
        var apiBaseUrl = $"{Request.Scheme}://{apiHost}";
        if (Request.Host.Port.HasValue)
        {
            apiBaseUrl += $":{Request.Host.Port.Value}";
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            apiBaseUrl,
            backendApiPath
        });

        var runtimeScripts = $"""
    <script>window.__AUTOMATON_APP__ = {payload};</script>
    <script src="/runtime/automaton-api.js"></script>
""";

        var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return headCloseIndex >= 0
            ? html.Insert(headCloseIndex, runtimeScripts)
            : runtimeScripts + html;
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
