using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runners")]
[Authorize(Policy = "Administrators")]
public sealed class AdminHostedRunnerStorageController(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    HostedRunnerStorageActionDeployment hostedRunnerStorageActionDeployment) : ControllerBase
{
    [HttpPost("runners/{id:guid}/verify-storage")]
    public async Task<ActionResult> VerifyStorage(Guid id, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.Status != HostedRunnerStatuses.Running || string.IsNullOrWhiteSpace(runner.ContainerId))
            return Conflict(new { message = "Storage verification is available only after the Hosted Runner is running and its container is provisioned." });
        if (!await hostedRunnerStorageActionDeployment.TryEnsureDeployedAsync(id, cancellationToken))
            return Conflict(new { message = "Storage verification requires an online Infrastructure Runner." });

        var window = DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(5).Ticks;
        var key = $"storage-verification:{id:N}:admin:{window}";
        var existing = await db.HostedRunnerStorageVerifications
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing != null) return Accepted(new { existing.Id, existing.Status });
        var verification = new HostedRunnerStorageVerification
        {
            Id = Guid.NewGuid(), HostedRunnerId = id, IdempotencyKey = key,
            Status = StorageVerificationStatuses.Pending, Reason = "AdministratorRequested",
            RequestedAtUtc = DateTime.UtcNow
        };
        db.HostedRunnerStorageVerifications.Add(verification);
        runner.StorageVerificationRequestedAtUtc = verification.RequestedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.HostedRunnerStorageVerificationRequested,
            Outcome = AuditOutcomes.Success,
            Message = $"Storage verification was requested for hosted Runner '{runner.Name}'.",
            TargetUserId = runner.UserId,
            Metadata = new { VerificationId = verification.Id, verification.Reason, runner.RunnerHostId }
        }, cancellationToken);
        return Accepted(new { verification.Id, verification.Status });
    }

}
