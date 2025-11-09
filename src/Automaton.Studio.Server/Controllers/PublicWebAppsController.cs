using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/web")]
[AllowAnonymous]
public class PublicWebAppsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProjectContextService _projectContextService;
    private readonly ILogger<PublicWebAppsController> _logger;

    public PublicWebAppsController(
        ApplicationDbContext context,
        ProjectContextService projectContextService,
        ILogger<PublicWebAppsController> logger)
    {
        _context = context;
        _projectContextService = projectContextService;
        _logger = logger;
    }

    /// <summary>
    /// Get published web app by slug with project-based access control
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublishedApp(string slug)
    {
        try
        {
            // Query published app by slug with project information
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
                    w.RequiresAuthentication, // Deprecated but kept for backwards compatibility
                    w.PublishingScope,
                    w.ProjectId
                })
                .FirstOrDefaultAsync();

            if (app == null)
            {
                _logger.LogWarning("Published app with slug '{Slug}' not found", slug);
                return NotFound(new { message = "App not found or not published" });
            }

            // Check authentication status
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = Guid.TryParse(userIdString, out var parsedUserId) ? parsedUserId : Guid.Empty;

            // Enforce PublishingScope access control
            switch (app.PublishingScope)
            {
                case PublishingScope.Anonymous:
                    // Public access - anyone can view
                    _logger.LogInformation("Public access to app '{AppName}' (slug: {Slug}) - Auth: {IsAuth}",
                        app.Name, slug, isAuthenticated);
                    break;

                case PublishingScope.ProjectMember:
                    // Requires authentication AND project membership
                    if (!isAuthenticated)
                    {
                        _logger.LogWarning("Unauthenticated access attempt to project-restricted app '{AppName}' (slug: {Slug})",
                            app.Name, slug);
                        return StatusCode(403, new 
                        { 
                            message = "This app requires authentication",
                            requiresAuth = true
                        });
                    }

                    // Check if user is a member of the app's project
                    if (app.ProjectId.HasValue)
                    {
                        var hasAccess = await _context.UserProjectRoles
                            .AnyAsync(upr => upr.UserId == userId && upr.ProjectId == app.ProjectId.Value);

                        if (!hasAccess)
                        {
                            _logger.LogWarning(
                                "User {UserId} attempted to access project-restricted app '{AppName}' (slug: {Slug}) " +
                                "but is not a member of project {ProjectId}",
                                userId, app.Name, slug, app.ProjectId.Value);
                            
                            return StatusCode(403, new 
                            { 
                                message = "You don't have access to this app. It is restricted to project members.",
                                requiresProjectMembership = true
                            });
                        }

                        _logger.LogInformation(
                            "User {UserId} accessing project-restricted app '{AppName}' (slug: {Slug}) - Project: {ProjectId}",
                            userId, app.Name, slug, app.ProjectId.Value);
                    }
                    else
                    {
                        // App has ProjectMember scope but no project assigned
                        // This shouldn't happen, but we'll treat it as requiring any authenticated user
                        _logger.LogWarning(
                            "App '{AppName}' (slug: {Slug}) has ProjectMember scope but no ProjectId",
                            app.Name, slug);
                    }
                    break;

                default:
                    _logger.LogWarning("Unknown PublishingScope {Scope} for app '{AppName}' (slug: {Slug})",
                        app.PublishingScope, app.Name, slug);
                    break;
            }

            // Deserialize definition to get HTML content
            var definition = JsonSerializer.Deserialize<Models.AppDefinition>(app.Definition ?? "{}");

            // Return the app data with access control metadata
            return Ok(new
            {
                app.Id,
                app.Name,
                app.DisplayName,
                app.Description,
                HtmlContent = definition?.HtmlContent ?? GetDefaultHtmlContent(),
                app.PublishedAt,
                app.RequiresAuthentication, // Deprecated but kept for backwards compatibility
                PublishingScope = app.PublishingScope.ToString(),
                IsUserAuthenticated = isAuthenticated
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
