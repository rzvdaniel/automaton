using System.Globalization;
using Automaton.Api.Authentication;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Starts a server backup for an app that holds the approved <c>backups:run</c> permission, on behalf of a viewer who is a
/// system administrator (the viewer token is bound to the app). The server still makes and encrypts the copy; the app
/// only learns about it through the existing backups read.
/// </summary>
[ApiController]
[Route("api/Install/backups/run")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.BackupsRunPolicy)]
public sealed class BackupRunController(IConfiguration configuration, ViewerTokenService viewerTokens,
    ResourceAuthorizationService authorization, IAuditEventService audit) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run(CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        if (viewer is null || !await authorization.IsSystemAdministratorAsync(viewer.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);

        // Audit first and fail closed, as for Studio's own backup request: a root-triggering request that cannot be recorded is not sent.
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ServerBackupRequested,
            Action = AuditActions.Requested,
            EntityType = AuditEntityTypes.ServerInstallation,
            ActorUserId = viewer,
            Message = "A server backup was requested by an app for a system administrator",
            Metadata = new { AppId = appId, Permission = "backups:run" },
            FailOnError = true
        }, token);

        var requestedBy = $"app {appId.ToString(null, CultureInfo.InvariantCulture)} for {viewer.Value}";
        return await InstallationBackups.RequestAsync(configuration, requestedBy, DateTimeOffset.UtcNow, token) switch
        {
            InstallationBackupRequestResult.Accepted => Accepted(new { requested = true }),
            InstallationBackupRequestResult.InProgress => Conflict(new { error = "A backup is already requested." }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "This server cannot start a backup from an app yet. An administrator can enable it with backup.sh --install-timer on the server." })
        };
    }
}
