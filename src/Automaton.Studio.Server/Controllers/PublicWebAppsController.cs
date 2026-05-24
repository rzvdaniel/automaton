using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for serving published web apps.
/// Apps are served from the frontend live file system (AppData/apps/{ownerId}/{appId}/live/frontend/).
///
/// Routes:
/// - GET /web/{slug}                  → Serves the published app as a top-level HTML document
/// - GET /web/{slug}/{path}           → Serves runtime files from the app scope
/// - GET /api/web/{slug}              → Returns app metadata + index.html content
/// - GET /api/web/{slug}/files/{path} → Returns any file from the app
/// </summary>
[ApiController]
[Route("api/web")]
[AllowAnonymous]
public class PublicWebAppsController : ControllerBase
{
    private const string PublishedApiScriptPath = "automaton-api.js";
    private static readonly Regex ExistingAutomatonApiScriptRegex = new(
        @"<script\b(?=[^>]*(?:id=[""']__automaton_api__[""']|automatonApi|APP_(?:ID|SLUG)))[^>]*>[\s\S]*?<\/script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RootAbsoluteAssetReferenceRegex = new(
        @"\b(href|src)=[""']\/(?!\/)([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    [HttpGet("/web/{slug}")]
    [HttpGet("/web/{slug}/")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServePublishedAppDocument(string slug)
    {
        var app = await FindPublishedAppAsync(slug);
        if (app == null)
        {
            _logger.LogWarning("Published app with slug '{Slug}' not found", slug);
            return NotFound("App not found or not published");
        }

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        if (app.RequiresAuthentication && !isAuthenticated)
        {
            WebAppCachePolicy.ApplyHtmlDocumentHeaders(Response);
            return Content(CreateAuthenticationRequiredHtml(slug), "text/html; charset=utf-8");
        }

        var htmlContent = await ReadPublishedAppHtmlAsync(app.OwnerId, app.Id);
        if (htmlContent == null)
        {
            _logger.LogWarning("Published app '{AppId}' has no live/frontend/index.html", app.Id);
            return NotFound("Runtime entry file not found");
        }

        try
        {
            await _appsService.IncrementViewCountAsync(app.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment view count for app {AppId}", app.Id);
        }

        WebAppCachePolicy.ApplyHtmlDocumentHeaders(Response);
        return Content(PreparePublishedAppHtml(htmlContent, slug), "text/html; charset=utf-8");
    }

    [HttpGet("/web/{slug}/{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServePublishedAppRuntimeFile(string slug, string? path)
    {
        if (path?.Equals(PublishedApiScriptPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            WebAppCachePolicy.ApplyHtmlDocumentHeaders(Response);
            return Content(CreatePublishedApiScript(slug), "application/javascript; charset=utf-8");
        }

        if (string.IsNullOrWhiteSpace(path) ||
            path.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            return await ServePublishedAppDocument(slug);
        }

        var app = await FindPublishedAppAsync(slug);
        if (app == null)
        {
            return NotFound(new { message = "App not found or not published" });
        }

        if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(path))
        {
            return NotFound(new { message = "File not found" });
        }

        if (!app.OwnerId.HasValue)
        {
            _logger.LogWarning("App '{AppId}' has no owner", app.Id);
            return NotFound(new { message = "App configuration error" });
        }

        var content = await _fileStorageService.ReadRuntimeFileBytesAsync(app.OwnerId.Value, app.Id, path);
        if (content == null)
        {
            return NotFound(new { message = "File not found" });
        }

        WebAppRuntimeResponsePolicy.ApplyPublicAssetCorsHeaders(Response);
        WebAppCachePolicy.ApplyPublishedFileHeaders(Response, Request, path, _webAppConfig);

        return File(content, WebAppRuntimeFilePolicy.GetContentType(path));
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

    private async Task<PublishedAppRuntimeInfo?> FindPublishedAppAsync(string slug)
    {
        return await _context.WebApps
            .Include(w => w.WebAppUsers)
            .Where(w => w.IsPublished && w.MarketplaceStatus == MarketplaceAppStatuses.Approved && w.PublicUrl == slug)
            .Select(w => new PublishedAppRuntimeInfo(
                w.Id,
                w.Name,
                w.RequiresAuthentication,
                w.WebAppUsers.FirstOrDefault() != null ? w.WebAppUsers.FirstOrDefault()!.UserId : (Guid?)null))
            .FirstOrDefaultAsync();
    }

    private async Task<string?> ReadPublishedAppHtmlAsync(Guid? ownerId, Guid appId)
    {
        if (!ownerId.HasValue)
        {
            _logger.LogWarning("App '{AppId}' has no owner", appId);
            return null;
        }

        return await _fileStorageService.ReadRuntimeFileAsync(ownerId.Value, appId, "index.html");
    }

    private string PreparePublishedAppHtml(string html, string slug)
    {
        html = ExistingAutomatonApiScriptRegex.Replace(html, string.Empty);
        html = RootAbsoluteAssetReferenceRegex.Replace(html, match => $"{match.Groups[1].Value}=\"{match.Groups[2].Value}\"");

        var appBasePath = $"/web/{Uri.EscapeDataString(slug)}/";
        var baseTag = $"<base href=\"{appBasePath}\">";
        var apiScript = $"<script id=\"__automaton_api__\" src=\"{appBasePath}{PublishedApiScriptPath}\"></script>";
        var injection = baseTag + apiScript;

        var headIndex = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var insertAt = headIndex + "<head>".Length;
            return html.Insert(insertAt, injection);
        }

        var bodyMatch = Regex.Match(html, @"<body[^>]*>", RegexOptions.IgnoreCase);
        if (bodyMatch.Success)
        {
            return html.Insert(bodyMatch.Index + bodyMatch.Length, injection);
        }

        return injection + html;
    }

    private string CreatePublishedApiScript(string slug)
    {
        var appSlug = JsonSerializer.Serialize(slug);
        var baseUrl = JsonSerializer.Serialize(GetPublicRequestOrigin());

        return $$"""
        'use strict';

        const APP_SLUG = {{appSlug}};
        const BASE_URL = {{baseUrl}};

        async function apiCall(path, method, body) {
          const response = await fetch(BASE_URL + path, {
            method,
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: body ? JSON.stringify(body) : undefined
          });

          const data = await response.json();
          if (!response.ok) {
            const error = new Error(data.error || data.message || 'Request failed');
            error.response = data;
            throw error;
          }

          return data;
        }

        async function executeFlow(flowIdentifier, { inputs = {}, runnerNames = null } = {}) {
          return apiCall(`/api/web-apps/${encodeURIComponent(APP_SLUG)}/flows/${encodeURIComponent(flowIdentifier)}/execute`, 'POST', { inputs, runnerNames });
        }

        async function executeController(controllerName, actionName, { inputs = {}, runnerNames = null } = {}) {
          return apiCall(`/api/web-apps/${encodeURIComponent(APP_SLUG)}/controllers/${encodeURIComponent(controllerName)}/actions/${encodeURIComponent(actionName)}/execute`, 'POST', { inputs, runnerNames });
        }

        async function callController(controllerName, actionName, inputs = {}) {
          const result = await executeController(controllerName, actionName, { inputs });
          if (!result.success) throw new Error(result.error || 'Request failed');
          return result.data;
        }

        async function uploadFile(file, { runnerNames = null } = {}) {
          const formData = new FormData();
          formData.append('file', file);

          const response = await fetch(`${BASE_URL}/api/web-apps/${encodeURIComponent(APP_SLUG)}/files/upload`, {
            method: 'POST',
            credentials: 'include',
            body: formData
          });

          const data = await response.json();
          if (!response.ok) throw new Error(data.error || 'File upload failed');
          return data;
        }

        function getDownloadUrl(sessionId) {
          return `${BASE_URL}/api/web-apps/${encodeURIComponent(APP_SLUG)}/files/${sessionId}`;
        }

        async function downloadFile(sessionId, filename) {
          const response = await fetch(getDownloadUrl(sessionId), { credentials: 'include' });
          if (!response.ok) throw new Error(`Download failed: ${response.status}`);

          const disposition = response.headers.get('content-disposition') || '';
          const name = filename || disposition.match(/filename="?([^";\n]+)/i)?.[1] || 'download';
          const blob = await response.blob();
          const objectUrl = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = objectUrl;
          anchor.download = name;
          anchor.click();
          setTimeout(() => URL.revokeObjectURL(objectUrl), 10000);
        }

        window.automatonApi = {
          executeFlow,
          callController,
          uploadFile,
          downloadFile,
          getAppId: () => null,
          getAppSlug: () => APP_SLUG,
          getBaseUrl: () => BASE_URL
        };
        """;
    }

    private string GetPublicRequestOrigin()
    {
        var scheme = Request.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto)
            ? forwardedProto.FirstOrDefault()
            : null;
        var host = Request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost)
            ? forwardedHost.FirstOrDefault()
            : null;

        scheme = string.IsNullOrWhiteSpace(scheme) ? Request.Scheme : scheme;
        host = string.IsNullOrWhiteSpace(host) ? Request.Host.Value : host;

        return $"{scheme}://{host}{Request.PathBase}".TrimEnd('/');
    }

    private static string CreateAuthenticationRequiredHtml(string slug)
    {
        var redirect = Uri.EscapeDataString($"/web/{slug}");
        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Authentication Required</title>
          <style>
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; font-family: system-ui, sans-serif; background: #f6f7f9; color: #1f2937; }
            main { width: min(420px, calc(100vw - 32px)); padding: 24px; background: white; border: 1px solid #dfe3ea; border-radius: 8px; box-shadow: 0 16px 40px rgb(15 23 42 / 10%); }
            h1 { margin: 0 0 8px; font-size: 24px; }
            p { margin: 0 0 20px; color: #4b5563; line-height: 1.5; }
            a { display: inline-flex; align-items: center; min-height: 40px; padding: 0 14px; color: white; background: #2563eb; border-radius: 6px; text-decoration: none; font-weight: 600; }
          </style>
        </head>
        <body>
          <main>
            <h1>Authentication required</h1>
            <p>This app requires you to sign in before it can open.</p>
            <a href="/auth/login?redirect={{redirect}}">Log in</a>
          </main>
        </body>
        </html>
        """;
    }

    private sealed record PublishedAppRuntimeInfo(Guid Id, string Name, bool RequiresAuthentication, Guid? OwnerId);
}
