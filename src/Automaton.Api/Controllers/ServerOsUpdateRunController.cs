using System.Globalization;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Installs OS updates for an app that holds the approved <c>server:update-os</c> permission, on behalf of a viewer who is a
/// system administrator (the viewer token is bound to the app). The host still runs apt and refuses while busy.
/// </summary>
[ApiController]
[Route("api/Install/os-updates/run")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.ServerUpdateOsPolicy)]
public sealed class ServerOsUpdateRunController(ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit,
    ViewerTokenService viewerTokens, ResourceAuthorizationService authorization)
    : ServerOsUpdateControllerBase(db, configuration, audit)
{
    [HttpPost]
    public async Task<IActionResult> Run([FromBody] StartServerOsUpdateRequest? request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        if (viewer is null || !await authorization.IsSystemAdministratorAsync(viewer.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);

        var requestedBy = $"app {appId.ToString(null, CultureInfo.InvariantCulture)} for {viewer.Value}";
        return await InstallAsync(viewer, requestedBy, "Installing OS updates was requested by an app for a system administrator",
            new { AppId = appId, Permission = "server:update-os", Scope = request?.Scope ?? "security" }, request?.Scope, token);
    }
}
