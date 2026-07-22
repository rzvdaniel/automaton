using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerBootstrapService
{
    private readonly ApplicationDbContext dbContext;
    private readonly HostedRunnerProvisioningService hostedRunnerProvisioningService;
    private readonly IEventPublisher eventPublisher;
    private readonly IApplicationEventService applicationEventService;

    public ManagedRunnerBootstrapService(
        ApplicationDbContext dbContext,
        HostedRunnerProvisioningService hostedRunnerProvisioningService,
        IEventPublisher eventPublisher,
        IApplicationEventService applicationEventService)
    {
        this.dbContext = dbContext;
        this.hostedRunnerProvisioningService = hostedRunnerProvisioningService;
        this.eventPublisher = eventPublisher;
        this.applicationEventService = applicationEventService;
    }

    public async Task<ManagedRunnerBootstrapResult> EnsureRunnerAssignmentAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var app = await dbContext.WebApps
            .FirstOrDefaultAsync(x => x.Id == appId, cancellationToken)
            ?? throw new InvalidOperationException("App not found.");

        if (app.ExecutionRunnerId.HasValue)
        {
            var assignedHostedRunner = await dbContext.HostedRunners
                .Include(x => x.Runner)
                .FirstOrDefaultAsync(x =>
                    x.RunnerId == app.ExecutionRunnerId.Value &&
                    x.UserId == ownerId &&
                    x.DeletedAt == null &&
                    x.Status != HostedRunnerStatuses.Disabled,
                    cancellationToken);
            var assignedRunner = assignedHostedRunner?.Runner;

            // Resolve assignments through the user's active HostedRunner record.
            // The underlying Runner row can remain online after its HostedRunner
            // has been soft-deleted, disabled, or reassigned.
            if (assignedRunner == null || !RunnerCapabilities.CanExecuteApplications(assignedRunner.CapabilitiesJson, assignedRunner.Purpose))
            {
                app.ExecutionRunnerId = null;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (RunnerPresenceEvaluator.IsOnline(assignedRunner.ConnectionId, assignedRunner.LastHeartbeatAtUtc, DateTime.UtcNow))
                {
                    return ManagedRunnerBootstrapResult.Ready(assignedRunner.Id);
                }

                return ManagedRunnerBootstrapResult.Unavailable(
                    "No connected runner is assigned to this app.");
            }

        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ownerId, cancellationToken)
            ?? throw new InvalidOperationException("App owner not found.");

        var hostedRunner = await dbContext.HostedRunners
            .Include(x => x.Runner)
            .FirstOrDefaultAsync(x =>
                x.UserId == ownerId &&
                x.DeletedAt == null &&
                x.Status != HostedRunnerStatuses.Disabled,
                cancellationToken);

        if (hostedRunner?.RunnerId is Guid runnerId &&
            hostedRunner.Runner is { } runner &&
            RunnerCapabilities.CanExecuteApplications(runner.CapabilitiesJson, runner.Purpose) &&
            RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            app.ExecutionRunnerId = runnerId;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ManagedRunnerBootstrapResult.Ready(runnerId);
        }

        if (hostedRunner?.Status == HostedRunnerStatuses.Failed)
        {
            var result = ManagedRunnerBootstrapResult.ProvisionFailed(
                string.IsNullOrWhiteSpace(hostedRunner.LastError)
                    ? "Managed runner could not be provisioned."
                    : $"Managed runner could not be provisioned. {hostedRunner.LastError}");
            await WriteManagedRuntimeEventAsync(appId, ApplicationEventTypes.ManagedRuntimeProvisionFailed, result.Message, cancellationToken);
            return result;
        }

        if (hostedRunner?.Status == HostedRunnerStatuses.Provisioning || hostedRunner?.Status == HostedRunnerStatuses.Pending)
        {
            return await GetInProgressResultAsync(appId, hostedRunner, cancellationToken);
        }

        HostedRunnerPreparationResult preparation;
        try
        {
            preparation = await hostedRunnerProvisioningService.PrepareProvisioningAsync(ownerId, Guid.Empty, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            var result = ManagedRunnerBootstrapResult.ProvisionFailed(ex.Message);
            await WriteManagedRuntimeEventAsync(appId, ApplicationEventTypes.ManagedRuntimeProvisionFailed, result.Message, cancellationToken);
            return result;
        }
        if (!preparation.ShouldPublishProvisionRequest)
        {
            if (preparation.HostedRunner.Status == HostedRunnerStatuses.Failed)
            {
                var result = ManagedRunnerBootstrapResult.ProvisionFailed(
                    string.IsNullOrWhiteSpace(preparation.HostedRunner.LastError)
                        ? "Managed runner could not be provisioned."
                        : $"Managed runner could not be provisioned. {preparation.HostedRunner.LastError}");
                await WriteManagedRuntimeEventAsync(appId, ApplicationEventTypes.ManagedRuntimeProvisionFailed, result.Message, cancellationToken);
                return result;
            }

            return await GetInProgressResultAsync(appId, preparation.HostedRunner, cancellationToken);
        }

        await eventPublisher.PublishAsync(
            new ManagedRunnerProvisionRequestedEvent
            {
                OperationId = preparation.OperationId,
                HostedRunnerId = preparation.HostedRunner.Id,
                UserId = ownerId,
                RunnerHostId = preparation.HostedRunner.RunnerHostId ?? Guid.Empty,
                SecretGrantIds = preparation.SecretGrantIds
            },
            cancellationToken);

        await WriteManagedRuntimeEventAsync(
            appId,
            ApplicationEventTypes.ManagedRuntimePreparing,
            "Managed runtime is being prepared.",
            cancellationToken);

        return ManagedRunnerBootstrapResult.Preparing(
            "Managed runtime is being prepared. Try deploying again in a moment.");
    }

    private async Task<ManagedRunnerBootstrapResult> GetInProgressResultAsync(
        Guid appId,
        HostedRunner hostedRunner,
        CancellationToken cancellationToken)
    {
        var message = string.IsNullOrWhiteSpace(hostedRunner.ContainerId)
            ? "Managed runtime is being prepared. Try deploying again in a moment."
            : "Runner provisioned but did not connect yet. Try deploying again in a moment.";
        var eventType = string.IsNullOrWhiteSpace(hostedRunner.ContainerId)
            ? ApplicationEventTypes.ManagedRuntimePreparing
            : ApplicationEventTypes.ManagedRuntimeWaitingForConnection;

        await WriteManagedRuntimeEventAsync(
            appId,
            eventType,
            message,
            cancellationToken);

        return string.IsNullOrWhiteSpace(hostedRunner.ContainerId)
            ? ManagedRunnerBootstrapResult.Preparing(message)
            : ManagedRunnerBootstrapResult.WaitingForConnection(message);
    }

    private Task WriteManagedRuntimeEventAsync(
        Guid appId,
        string eventType,
        string message,
        CancellationToken cancellationToken)
    {
        return applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = appId,
            Level = ApplicationEventLevels.Info,
            Category = ApplicationEventCategories.Deployment,
            EventType = eventType,
            Message = message
        }, cancellationToken);
    }
}

public sealed class ManagedRunnerBootstrapResult
{
    public bool IsReady { get; init; }
    public Guid? RunnerId { get; init; }
    public string State { get; init; } = AppDeployStates.Ready;
    public string Message { get; init; } = string.Empty;
    public bool RequiresRetry { get; init; }

    public static ManagedRunnerBootstrapResult Ready(Guid runnerId) => new()
    {
        IsReady = true,
        RunnerId = runnerId,
        State = AppDeployStates.Ready
    };

    public static ManagedRunnerBootstrapResult Preparing(string message) => new()
    {
        State = AppDeployStates.PreparingManagedRuntime,
        Message = message,
        RequiresRetry = true
    };

    public static ManagedRunnerBootstrapResult WaitingForConnection(string message) => new()
    {
        State = AppDeployStates.ManagedRunnerWaitingForConnection,
        Message = message,
        RequiresRetry = true
    };

    public static ManagedRunnerBootstrapResult ProvisionFailed(string message) => new()
    {
        State = AppDeployStates.ManagedRunnerProvisionFailed,
        Message = message,
        RequiresRetry = true
    };

    public static ManagedRunnerBootstrapResult Unavailable(string message) => new()
    {
        State = AppDeployStates.ManagedRuntimeUnavailable,
        Message = message
    };
}
