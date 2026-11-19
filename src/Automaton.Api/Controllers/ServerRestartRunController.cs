using System.Globalization;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Restarts the server for an app that holds the approved <c>server:restart</c> permission, on behalf of a viewer who is a
/// system administrator (the viewer token is bound to the app). The host still does the reboot and refuses while busy.
/// </summary>
[ApiController]
[Route("api/Install/restart/run")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.ServerRestartPolicy)]
public sealed class ServerRestartRunController(ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit,
    ViewerTokenService viewerTokens, ResourceAuthorizationService authorization)
    : ServerRestartControllerBase(db, configuration, audit)
{
    [HttpPost]
    public async Task<IActionResult> Run([FromBody] StartServerRestartRequest? request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        if (viewer is null || !await authorization.IsSystemAdministratorAsync(viewer.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);

        var requestedBy = $"app {appId.ToString(null, CultureInfo.InvariantCulture)} for {viewer.Value}";
        return await RestartAsync(viewer, requestedBy, "A server restart was requested by an app for a system administrator",
            new { AppId = appId, Permission = "server:restart", RestartAtUtc = request?.RestartAtUtc }, request?.RestartAtUtc, token);
    }
}
