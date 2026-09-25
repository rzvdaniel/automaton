using Automaton.Api.Attributes;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("/")]
[AllowAnonymous]
public sealed class UserAppMediaController(
    AppsService appsService,
    AppMediaStorageService mediaStorageService,
    ILogger<UserAppMediaController> logger) : ControllerBase
{
    [HttpGet("apps/{slug}/media/{*path}")]
    public Task<IActionResult> ServeStudioHostedAppMedia(string slug, string path, CancellationToken cancellationToken)
    {
        return ServePublishedAppMedia(slug, path, cancellationToken);
    }

    [HttpGet("{slug}/media/{*path}")]
    [AppsHostOnly]
    public Task<IActionResult> ServeDedicatedAppsHostAppMedia(string slug, string path, CancellationToken cancellationToken)
    {
        return ServePublishedAppMedia(slug, path, cancellationToken);
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
        return statusCode == StatusCodes.Status200OK
            ? new EmptyResult()
            : StatusCode(statusCode);
    }
}
