using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The shared request flow for restarting the server: audit first, then the host request file.</summary>
public abstract class ServerRestartControllerBase(
    ApplicationDbContext db, IConfiguration configuration, IAuditEventService audit) : ControllerBase
{
    protected async Task<IActionResult> RestartAsync(Guid? actorUserId, string requestedBy, string message, object metadata,
        DateTimeOffset? restartAt, CancellationToken token)
    {
        // Audit first and fail closed, as for backups and server updates: a root-triggering request that cannot be recorded is not sent.
        await audit.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.ServerRestartRequested,
            Action = AuditActions.Requested,
            EntityType = AuditEntityTypes.ServerInstallation,
            ActorUserId = actorUserId,
            Message = message,
            Metadata = metadata,
            FailOnError = true
        }, token);

        var (result, reason) = await ServerRestart.RequestAsync(db, configuration, requestedBy, restartAt, DateTimeOffset.UtcNow, token);
        return result switch
        {
            ServerRestartResult.Accepted => Accepted(new { requested = true, restartAtUtc = restartAt }),
            ServerRestartResult.InProgress => Conflict(new { message = "A restart is already requested or scheduled." }),
            ServerRestartResult.Busy => Conflict(new { message = reason }),
            ServerRestartResult.InvalidTime => BadRequest(new { message = "The restart time must be within the next 7 days." }),
            ServerRestartResult.TriggerBlocked => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The restart request directory on the server cannot be used. Check /var/lib/automaton/restart/requests: it must be writable and must not contain a request.json that is not a regular file."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "This server cannot restart itself from Studio yet. Run sudo bash /opt/automaton-server/update.sh once on the server to enable it."
            })
        };
    }
}
