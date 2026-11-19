using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The shared request flow for installing OS updates: audit first, then the host request file.</summary>
public abstract class ServerOsUpdateControllerBase(
    ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit) : ControllerBase
{
    protected async Task<IActionResult> InstallAsync(Guid? actorUserId, string requestedBy, string message, object metadata,
        string? scope, CancellationToken token)
    {
        if (scope is not null && !ServerOsUpdate.Scopes.Contains(scope, StringComparer.Ordinal))
            return BadRequest(new { message = "The scope must be \"security\" or \"all\"." });
        // Audit first and fail closed, as for backups, server updates and restarts: a root-triggering request that cannot be recorded is not sent.
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ServerOsUpdateRequested,
            Action = AuditActions.Requested,
            EntityType = AuditEntityTypes.ServerInstallation,
            ActorUserId = actorUserId,
            Message = message,
            Metadata = metadata,
            FailOnError = true
        }, token);

        var (result, reason) = await ServerOsUpdate.RequestAsync(db, configuration, requestedBy, scope, DateTimeOffset.UtcNow, token);
        return result switch
        {
            ServerOsUpdateResult.Accepted => Accepted(new { requested = true, scope = scope ?? "security" }),
            ServerOsUpdateResult.InProgress => Conflict(new { message = "An OS update is already requested or running." }),
            ServerOsUpdateResult.Busy => Conflict(new { message = reason }),
            ServerOsUpdateResult.InvalidScope => BadRequest(new { message = "The scope must be \"security\" or \"all\"." }),
            ServerOsUpdateResult.TriggerBlocked => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The OS update request directory on the server cannot be used. Check /var/lib/automaton/os-update/requests: it must be writable and must not contain a request.json that is not a regular file."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "This server cannot install OS updates from Studio yet. Run sudo bash /opt/automaton-server/update.sh once on the server to enable it."
            })
        };
    }
}
