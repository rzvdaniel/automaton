using System.Text.Json;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/hosted-runners")]
[Authorize(Policy = "Administrators")]
public sealed class AdminHostedRunnerStorageController(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    IEventPublisher eventPublisher,
    ITransactionalEventExecutor transactionalEventExecutor) : ControllerBase
{
    [HttpPost("runners/{id:guid}/verify-storage")]
    public async Task<ActionResult> VerifyStorage(Guid id, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.Status != HostedRunnerStatuses.Running || string.IsNullOrWhiteSpace(runner.ContainerId))
            return Conflict(new { message = "Storage verification is available only after the Hosted Runner is running and its container is provisioned." });
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

    [HttpPost("runners/{id:guid}/suspend")]
    public Task<ActionResult> Suspend(Guid id, CancellationToken cancellationToken) => QueueEnforcementActionAsync(id, StorageEnforcementActions.SuspendHostedRunner, cancellationToken);

    [HttpPost("runners/{id:guid}/resume")]
    public Task<ActionResult> Resume(Guid id, CancellationToken cancellationToken) => QueueEnforcementActionAsync(id, StorageEnforcementActions.ResumeHostedRunner, cancellationToken);

    [HttpPost("runners/{id:guid}/extend-grace")]
    public async Task<ActionResult> ExtendGrace(Guid id, ExtendStorageGracePeriodRequest request, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.EnforcementState is HostedRunnerEnforcementStates.Suspended or HostedRunnerEnforcementStates.ResumePending or HostedRunnerEnforcementStates.SuspensionPending)
            return Conflict(new { message = "Extend grace is unavailable while a suspension or resume operation is active." });
        runner.EnforcementState = HostedRunnerEnforcementStates.GracePeriod;
        runner.GracePeriodEndsAtUtc = DateTime.UtcNow.AddHours(Math.Clamp(request.Hours, 1, 720));
        runner.LastEnforcementReason = "Grace period extended by an administrator.";
        runner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await WriteEnforcementAuditAsync(runner, StorageTelemetryAuditEvents.EnforcementGraceExtended, "Grace period extended by an administrator.", cancellationToken);
        return Ok(new { runner.Id, runner.EnforcementState, runner.GracePeriodEndsAtUtc });
    }

    [HttpPost("runners/{id:guid}/clear-enforcement")]
    public async Task<ActionResult> ClearEnforcement(Guid id, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        if (runner.EnforcementState is HostedRunnerEnforcementStates.Suspended or HostedRunnerEnforcementStates.ResumePending or HostedRunnerEnforcementStates.SuspensionPending)
            return Conflict(new { message = "Resume the Runner and complete its lifecycle operation before clearing enforcement state." });
        runner.EnforcementState = HostedRunnerEnforcementStates.None;
        runner.OverLimitSinceUtc = null;
        runner.GracePeriodEndsAtUtc = null;
        runner.LastEnforcementReason = "Enforcement state cleared by an administrator.";
        runner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await WriteEnforcementAuditAsync(runner, StorageTelemetryAuditEvents.EnforcementCleared, "Enforcement state cleared by an administrator.", cancellationToken);
        return Ok(new { runner.Id, runner.EnforcementState });
    }

    private async Task<ActionResult> QueueEnforcementActionAsync(Guid id, string actionKey, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners.Include(x => x.RunnerHost).FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken);
        if (runner == null) return NotFound();
        var action = GetEnforcementDeploymentAction(actionKey);
        var existing = await db.HostedRunnerDeployments.FirstOrDefaultAsync(x => x.HostedRunnerId == id && x.Action == action &&
            (x.Status == HostedRunnerDeploymentStatuses.Pending || x.Status == HostedRunnerDeploymentStatuses.Queued || x.Status == HostedRunnerDeploymentStatuses.Dispatching || x.Status == HostedRunnerDeploymentStatuses.Executing || x.Status == HostedRunnerDeploymentStatuses.Running || x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect), cancellationToken);
        if (existing != null) return Accepted(new { existing.Id, existing.Status });
        var validationFailure = ValidateEnforcementRequest(runner, action);
        if (validationFailure != null) return validationFailure;
        var operation = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = id, Action = action, Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = DateTime.UtcNow, Logs = "Administrator requested storage enforcement action." };
        var message = CreateEnforcementCommandMessage(runner, operation, actionKey);
        await PersistEnforcementCommandAsync(runner, operation, action, message, cancellationToken);
        await WriteEnforcementAuditAsync(runner, GetEnforcementRequestedEventType(actionKey),
            $"Administrator requested '{actionKey}'.", cancellationToken);
        return Accepted(new { operation.Id, operation.Status });
    }

    private static string GetEnforcementDeploymentAction(string actionKey)
        => actionKey == StorageEnforcementActions.SuspendHostedRunner
            ? HostedRunnerDeploymentActions.Suspend
            : HostedRunnerDeploymentActions.Resume;

    private static ActionResult? ValidateEnforcementRequest(HostedRunner runner, string action)
    {
        if (runner.RunnerHostId == null)
            return new BadRequestObjectResult(new { message = "The hosted Runner has no assigned Runner Host." });
        if (!runner.RunnerHost!.InfrastructureRunnerId.HasValue)
            return new BadRequestObjectResult(new { message = "The hosted Runner host has no assigned Infrastructure Runner." });
        if (action == HostedRunnerDeploymentActions.Resume && runner.EnforcementState != HostedRunnerEnforcementStates.Suspended)
            return new ConflictObjectResult(new { message = "Resume is only available for a suspended Runner." });
        if (action == HostedRunnerDeploymentActions.Suspend &&
            runner.EnforcementState is HostedRunnerEnforcementStates.Suspended or HostedRunnerEnforcementStates.ResumePending or HostedRunnerEnforcementStates.SuspensionPending)
            return new ConflictObjectResult(new { message = "The Runner is already suspended or has a suspension operation in progress." });
        return null;
    }

    private static InfrastructureCommandRequestedEvent CreateEnforcementCommandMessage(
        HostedRunner runner,
        HostedRunnerDeployment operation,
        string actionKey)
        => new(
            CommandId: operation.Id,
            OperationId: operation.Id,
            RunnerHostId: runner.RunnerHostId!.Value,
            InfrastructureRunnerId: runner.RunnerHost!.InfrastructureRunnerId!.Value,
            ActionKey: actionKey,
            InputJson: JsonSerializer.Serialize(new { operationId = operation.Id, hostedRunnerId = runner.Id, expectedContainerName = runner.ContainerName }),
            SecretGrantIds: [],
            CreatedAtUtc: DateTime.UtcNow);

    private async Task PersistEnforcementCommandAsync(
        HostedRunner runner,
        HostedRunnerDeployment operation,
        string action,
        InfrastructureCommandRequestedEvent message,
        CancellationToken cancellationToken)
    {
        await transactionalEventExecutor.ExecuteAsync(async token =>
        {
            db.HostedRunnerDeployments.Add(operation);
            runner.EnforcementState = action == HostedRunnerDeploymentActions.Suspend
                ? HostedRunnerEnforcementStates.SuspensionPending
                : HostedRunnerEnforcementStates.ResumePending;
            runner.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            await eventPublisher.PublishAsync(message, token);
        }, cancellationToken);
    }

    private static string GetEnforcementRequestedEventType(string actionKey)
        => actionKey == StorageEnforcementActions.SuspendHostedRunner
            ? StorageTelemetryAuditEvents.EnforcementSuspensionRequested
            : StorageTelemetryAuditEvents.EnforcementResumeRequested;

    private Task WriteEnforcementAuditAsync(HostedRunner runner, string eventType, string message, CancellationToken cancellationToken) =>
        auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Changed, EntityType = AuditEntityTypes.HostedRunner, EntityId = runner.Id,
            EventType = eventType, Outcome = AuditOutcomes.Success, Message = message,
            TargetUserId = runner.UserId, Metadata = new { runner.EnforcementState, runner.StorageState }
        }, cancellationToken);

}
