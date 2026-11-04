using System.Security.Claims;
using Automaton.Api.Constants;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Server updates for system administrators (Studio and CLI). The update itself runs on the host as root;
/// this controller only writes a request file and reads the status the host reports.
/// </summary>
[ApiController]
[Route("api/install/update")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
public class InstallUpdateController(IConfiguration configuration, IAuditEventService auditEventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InstallationUpdateStatus>> Get(CancellationToken token)
        => Ok(await InstallationUpdate.ReadAsync(configuration, DateTimeOffset.UtcNow, token));

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] StartInstallationUpdateRequest? request, CancellationToken token)
    {
        var version = string.IsNullOrWhiteSpace(request?.Version) ? InstallationUpdate.Latest : request.Version.Trim();
        if (!InstallationUpdate.IsValidVersion(version))
            return BadRequest(new { message = "The version must be \"latest\" or a release tag such as server-v0.2.0." });
        var allowDowngrade = request?.AllowDowngrade == true;

        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var requestedBy = string.IsNullOrWhiteSpace(email) ? userId.ToString() : $"{email} {userId}";

        // Audit first and fail closed: a root-triggering request that cannot be recorded must not be sent.
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ServerUpdateRequested,
            Action = AuditActions.Requested,
            EntityType = AuditEntityTypes.ServerInstallation,
            ActorUserId = userId == Guid.Empty ? null : userId,
            Message = "A server update was requested",
            Metadata = new
            {
                RequestedVersion = version,
                AllowDowngrade = allowDowngrade,
                AuthenticationType = User.Identity?.AuthenticationType,
                ApiKeyScope = User.FindFirstValue("api_key_scope"),
                ApiKeyId = User.FindFirstValue("api_key_id")
            },
            FailOnError = true
        }, token);

        return await InstallationUpdate.RequestAsync(configuration, version, allowDowngrade, requestedBy, DateTimeOffset.UtcNow, token) switch
        {
            InstallationUpdateRequestResult.Accepted => Accepted(new { requestedVersion = version }),
            InstallationUpdateRequestResult.InProgress => Conflict(new { message = "An update is already running or requested." }),
            InstallationUpdateRequestResult.Downgrade => BadRequest(new
            {
                message = "That release is older than the installed one. Request a downgrade explicitly with allowDowngrade (CLI: --allow-downgrade)."
            }),
            InstallationUpdateRequestResult.TriggerBlocked => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The update request directory on the server cannot be used. Check /var/lib/automaton/update/requests: it must be writable and must not contain a request.json that is not a regular file."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "This server cannot update itself from Studio yet. Run sudo bash /opt/automaton-server/update.sh once on the server to enable it."
            })
        };
    }
}

public sealed record StartInstallationUpdateRequest(string? Version, bool AllowDowngrade = false);
