using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class HostedRunnerManagementService
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerProvisioningService provisioningService;
    private readonly IEventPublisher eventPublisher;
    private readonly IAuditEventService auditEventService;
    private readonly UserContextService userContextService;
    private readonly ILogger<HostedRunnerManagementService> logger;

    private Guid UserId => userContextService.GetUserId();

    public HostedRunnerManagementService(
        ApplicationDbContext db,
        HostedRunnerProvisioningService provisioningService,
        IEventPublisher eventPublisher,
        IAuditEventService auditEventService,
        UserContextService userContextService,
        ILogger<HostedRunnerManagementService> logger)
    {
        this.db = db;
        this.provisioningService = provisioningService;
        this.eventPublisher = eventPublisher;
        this.auditEventService = auditEventService;
        this.userContextService = userContextService;
        this.logger = logger;
    }

    public async Task<HostedRunner> RestartAsAdminAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        return await RestartCoreAsync(hostedRunner, cancellationToken);
    }

    public async Task<HostedRunner> ReprovisionAsAdminAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        return await ReprovisionCoreAsync(hostedRunner, cancellationToken);
    }

    public async Task DeleteAsAdminAsync(
        Guid hostedRunnerId,
        string disposition,
        CancellationToken cancellationToken)
    {
        if (!HostedRunnerDataDispositions.IsValid(disposition))
        {
            throw new ArgumentException("Data disposition must be 'preserve' or 'delete'.", nameof(disposition));
        }

        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        if (hostedRunner.RunnerHost == null)
            throw new InvalidOperationException("Hosted runner has no assigned host.");
        if (!hostedRunner.RunnerHost.InfrastructureRunnerId.HasValue)
            throw new InvalidOperationException("Hosted runner host has no assigned Infrastructure Runner.");
        hostedRunner.Status = HostedRunnerStatuses.Deleting;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        var appAssignments = await db.AppDeploymentTargets
            .Where(x => x.ExecutionRunnerId == hostedRunner.RunnerId)
            .ToListAsync(cancellationToken);
        foreach (var target in appAssignments)
        {
            target.ExecutionRunnerId = null;
            target.GenericRuntimeDeployed = false;
            target.Status = DeploymentTargetStatus.Unassigned;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var deployment = AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Deprovision,
            HostedRunnerDeploymentStatuses.Queued,
            "Trusted infrastructure Action: delete-hosted-runner.",
            "Hosted runner deletion requested.");
        await db.SaveChangesAsync(cancellationToken);
        await PublishDeleteActionAsync(hostedRunner, deployment, disposition, cancellationToken);
    }

    public async Task RestartOwnedHostedRunnerByRunnerIdAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
                .ThenInclude(x => x!.InfrastructureRunner)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x =>
                x.RunnerId == runnerId &&
                x.UserId == UserId &&
                x.DeletedAt == null,
                cancellationToken);

        if (hostedRunner == null)
        {
            throw new InvalidOperationException("Hosted runner not found for this runner.");
        }

        await RestartCoreAsync(hostedRunner, cancellationToken);
    }

    private async Task<HostedRunner> RestartCoreAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        var host = hostedRunner.RunnerHost;
        if (host == null)
        {
            throw new InvalidOperationException("Hosted runner has no assigned host.");
        }
        if (!host.InfrastructureRunnerId.HasValue)
        {
            throw new InvalidOperationException("Hosted runner host has no assigned Infrastructure Runner.");
        }
        if (!hostedRunner.RunnerId.HasValue)
        {
            throw new InvalidOperationException("Hosted runner has no linked Runner identity to confirm after restart.");
        }

        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Requested,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.RunnerRestartRequested,
                Message = $"Hosted runner '{hostedRunner.Name}' restart was requested",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    RunnerHostName = host.Name,
                    hostedRunner.ContainerName
                }
            },
            cancellationToken);

        var deployment = AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Restart,
            HostedRunnerDeploymentStatuses.Queued,
            "Trusted infrastructure Action: restart-hosted-runner.",
            "Restart requested.");
        deployment.ExpectedRunnerId = hostedRunner.RunnerId;
        deployment.PreviousConnectionId = hostedRunner.Runner?.ConnectionId;
        deployment.ReconnectAfterUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await PublishRestartActionAsync(hostedRunner, deployment, cancellationToken);
        logger.LogInformation("Hosted runner {HostedRunnerId} restart queued on host {HostId}", hostedRunner.Id, host.Id);
        return hostedRunner;
    }

    private async Task<HostedRunner> ReprovisionCoreAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Requested,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.RunnerReprovisionRequested,
                Message = $"Hosted runner '{hostedRunner.Name}' reprovision was requested",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    hostedRunner.HostedRunnerSpecificationId,
                    hostedRunner.ContainerName
                }
            },
            cancellationToken);

        var preparation = await provisioningService.PrepareProvisioningAsync(
            hostedRunner.UserId,
            Guid.Empty,
            cancellationToken,
            forceProvision: true,
            requiredHostedRunnerId: hostedRunner.Id);
        if (preparation.ShouldPublishProvisionRequest)
        {
            var preparedRunner = preparation.HostedRunner;
            if (preparedRunner.Runner != null)
                preparedRunner.Runner.ConnectionId = null;

            preparedRunner.RunnerId = null;
            preparedRunner.LastError = null;
            preparedRunner.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var reprovisioned = await provisioningService.DispatchPreparedProvisioningAsync(preparation, cancellationToken);

        if (reprovisioned == null)
        {
            await auditEventService.WriteAsync(
                new AuditEventWriteRequest
                {
                    Action = AuditActions.Failed,
                    EntityType = AuditEntityTypes.HostedRunner,
                    EntityId = hostedRunner.Id,
                    EventType = AuditEventTypes.RunnerReprovisionFailed,
                    Outcome = AuditOutcomes.Failed,
                    Message = $"Hosted runner '{hostedRunner.Name}' reprovision could not start",
                    TargetUserId = hostedRunner.UserId,
                    Metadata = new
                    {
                        hostedRunner.Name,
                        hostedRunner.RunnerId,
                        hostedRunner.RunnerHostId,
                        hostedRunner.HostedRunnerSpecificationId
                    }
                },
                cancellationToken);
            throw new InvalidOperationException("Hosted runner reprovisioning could not start.");
        }

        return reprovisioned;
    }

    private async Task<HostedRunner> LoadHostedRunnerAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
                .ThenInclude(x => x!.InfrastructureRunner)
            .Include(x => x.Runner)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.Id == hostedRunnerId && x.DeletedAt == null, cancellationToken);

        if (hostedRunner == null)
        {
            throw new InvalidOperationException("Hosted runner not found.");
        }

        return hostedRunner;
    }

    private HostedRunnerDeployment AddDeployment(
        HostedRunner runner,
        string action,
        string status,
        string? commandPreview,
        string? message)
    {
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = runner.Id,
            Action = action,
            Status = status,
            CommandPreview = commandPreview,
            Logs = status is HostedRunnerDeploymentStatuses.Failed ? null : message,
            Error = status is HostedRunnerDeploymentStatuses.Failed ? message : null,
            StartedAt = DateTime.UtcNow,
            FinishedAt = status is HostedRunnerDeploymentStatuses.Failed or HostedRunnerDeploymentStatuses.Skipped
                ? DateTime.UtcNow
                : null
        };

        db.HostedRunnerDeployments.Add(deployment);
        return deployment;
    }

    private Task PublishRestartActionAsync(
        HostedRunner runner,
        HostedRunnerDeployment deployment,
        CancellationToken cancellationToken)
    {
        var hostId = runner.RunnerHostId ?? throw new InvalidOperationException("Hosted runner has no assigned host.");
        var infrastructureRunnerId = runner.RunnerHost?.InfrastructureRunnerId
            ?? throw new InvalidOperationException("Hosted runner host has no assigned Infrastructure Runner.");
        return eventPublisher.PublishAsync(new InfrastructureCommandRequestedEvent(
            CommandId: deployment.Id,
            OperationId: deployment.Id,
            RunnerHostId: hostId,
            InfrastructureRunnerId: infrastructureRunnerId,
            ActionKey: "restart-hosted-runner",
            InputJson: JsonSerializer.Serialize(new
            {
                operationId = deployment.Id,
                hostedRunnerId = runner.Id,
                containerName = runner.ContainerName
            }),
            SecretGrantIds: [],
            CreatedAtUtc: DateTime.UtcNow), cancellationToken);
    }

    private Task PublishDeleteActionAsync(
        HostedRunner runner,
        HostedRunnerDeployment deployment,
        string disposition,
        CancellationToken cancellationToken)
    {
        var hostId = runner.RunnerHostId ?? throw new InvalidOperationException("Hosted runner has no assigned host.");
        var infrastructureRunnerId = runner.RunnerHost?.InfrastructureRunnerId
            ?? throw new InvalidOperationException("Hosted runner host has no assigned Infrastructure Runner.");
        return eventPublisher.PublishAsync(new InfrastructureCommandRequestedEvent(
            CommandId: deployment.Id,
            OperationId: deployment.Id,
            RunnerHostId: hostId,
            InfrastructureRunnerId: infrastructureRunnerId,
            ActionKey: "delete-hosted-runner",
            InputJson: JsonSerializer.Serialize(new
            {
                operationId = deployment.Id,
                hostedRunnerId = runner.Id,
                containerName = runner.ContainerName,
                preserveVolumes = disposition == HostedRunnerDataDispositions.Preserve,
                systemVolumeName = $"automaton-hosted-runner-{runner.Id:N}-system",
                userVolumeName = $"automaton-hosted-runner-{runner.Id:N}-user"
            }),
            SecretGrantIds: [],
            CreatedAtUtc: DateTime.UtcNow), cancellationToken);
    }
}
