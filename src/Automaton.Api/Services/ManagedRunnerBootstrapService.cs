using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Constants;
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
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;

    public ManagedRunnerBootstrapService(
        ApplicationDbContext dbContext,
        HostedRunnerProvisioningService hostedRunnerProvisioningService,
        IEventPublisher eventPublisher,
        IApplicationEventService applicationEventService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy)
    {
        this.dbContext = dbContext;
        this.hostedRunnerProvisioningService = hostedRunnerProvisioningService;
        this.eventPublisher = eventPublisher;
        this.applicationEventService = applicationEventService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
    }

    public async Task<ManagedRunnerBootstrapResult> EnsureRunnerAssignmentAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var assignedRunner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
                appId,
                ownerId,
                requireOnline: false,
                cancellationToken: cancellationToken);

            // Self-hosted application runners intentionally have no HostedRunner row.
            // Hosted Runner lifecycle checks only apply when the assigned runner is managed.
            var assignedHostedRunner = await dbContext.HostedRunners
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.RunnerId == assignedRunner.Id &&
                    x.UserId == ownerId,
                    cancellationToken);
            if (assignedHostedRunner != null)
            {
                if (assignedHostedRunner.DeletedAt != null ||
                    assignedHostedRunner.Status is HostedRunnerStatuses.Disabled or HostedRunnerStatuses.Deleted or HostedRunnerStatuses.Deleting)
                {
                    return ManagedRunnerBootstrapResult.Unavailable(
                        "The assigned Hosted Runner is no longer eligible for App execution.");
                }

                if (assignedHostedRunner.Status == HostedRunnerStatuses.Failed)
                {
                    return ManagedRunnerBootstrapResult.ProvisionFailed(
                        string.IsNullOrWhiteSpace(assignedHostedRunner.LastError)
                            ? "The assigned Hosted Runner failed to provision."
                            : $"The assigned Hosted Runner failed to provision. {assignedHostedRunner.LastError}");
                }

                if (assignedHostedRunner.Status is HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning or HostedRunnerStatuses.Reprovisioning)
                {
                    return await GetInProgressResultAsync(appId, assignedHostedRunner, cancellationToken);
                }

                if (assignedHostedRunner.Status != HostedRunnerStatuses.Running)
                {
                    return ManagedRunnerBootstrapResult.Unavailable(
                        "The assigned Hosted Runner is not available for App execution.");
                }

                RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(assignedRunner);
            }

            return RunnerPresenceEvaluator.IsOnline(assignedRunner.ConnectionId, assignedRunner.LastHeartbeatAtUtc, DateTime.UtcNow)
                ? ManagedRunnerBootstrapResult.Ready(assignedRunner.Id)
                : ManagedRunnerBootstrapResult.Unavailable("The assigned Runner is offline.");
        }
        catch (AppDeploymentTargetUnassignedException)
        {
            // Provisioning below creates and assigns the user's managed Runner.
        }
        catch (UnauthorizedAccessException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
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
            SupportsContainerApplicationExecution(runner) &&
            RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            var app = await dbContext.WebApps
                .Include(item => item.DeploymentTargets)
                .SingleAsync(item => item.Id == appId, cancellationToken);
            await runnerAssignmentPolicy.AssignAsync(app, runnerId, ownerId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ManagedRunnerBootstrapResult.Ready(runnerId);
        }

        if (hostedRunner?.Status == HostedRunnerStatuses.Running &&
            hostedRunner.Runner is { } incompatibleRunner &&
            RunnerCapabilities.CanExecuteApplications(incompatibleRunner.CapabilitiesJson, incompatibleRunner.Purpose) &&
            !SupportsContainerApplicationExecution(incompatibleRunner))
        {
            const string message = "No compatible application Runner is currently available. The assigned Hosted Runner requires an upgrade for the current Generic Runtime deployment protocol.";
            return ManagedRunnerBootstrapResult.Unavailable(message);
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

    private static bool SupportsContainerApplicationExecution(Runner runner)
        => int.TryParse(runner.ApiCompatibilityVersion, out var version)
            && version >= int.Parse(RunnerApiCompatibilityVersions.ContainerApplicationExecution);

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
