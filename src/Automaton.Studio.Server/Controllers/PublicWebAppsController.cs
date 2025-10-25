using Automaton.Studio.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/web")]
[AllowAnonymous]
public class PublicWebAppsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PublicWebAppsController> _logger;

    public PublicWebAppsController(
        ApplicationDbContext context,
        ILogger<PublicWebAppsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get published web app by slug (public access, authentication checked client-side)
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublishedApp(string slug)
    {
        try
        {
            // Query published app by slug
            var app = await _context.WebApps
                .Where(w => w.IsPublished && w.PublicUrl == slug)
                .Select(w => new
                {
                    w.Id,
                    w.Name,
                    w.DisplayName,
                    w.Description,
                    w.Definition,
                    w.PublishedAt,
                    w.RequiresAuthentication
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

            // Deserialize definition to get HTML content
            var definition = JsonSerializer.Deserialize<Models.AppDefinition>(app.Definition ?? "{}");

            // Always return the app data with RequiresAuthentication flag
            // Client-side will handle the authentication check and show appropriate UI
            return Ok(new
            {
                app.Id,
                app.Name,
                app.DisplayName,
                app.Description,
                HtmlContent = definition?.HtmlContent ?? GetDefaultHtmlContent(),
                app.PublishedAt,
                app.RequiresAuthentication
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving published app with slug '{Slug}'", slug);
            return StatusCode(500, new { message = "Internal server error" });
        }
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
