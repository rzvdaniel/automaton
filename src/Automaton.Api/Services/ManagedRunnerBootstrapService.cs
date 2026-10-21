using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerBootstrapService
{
    private readonly ApplicationDbContext dbContext;
    private readonly IApplicationEventService applicationEventService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;

    public ManagedRunnerBootstrapService(
        ApplicationDbContext dbContext,
        IApplicationEventService applicationEventService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy)
    {
        this.dbContext = dbContext;
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
            return await GetAssignedRunnerResultAsync(appId, ownerId, cancellationToken);
        }
        catch (AppDeploymentTargetUnassignedException)
        {
            return ManagedRunnerBootstrapResult.Unavailable(
                "Assign a Management Runner to this app before deploying.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
        }

    }

    private async Task<ManagedRunnerBootstrapResult> GetAssignedRunnerResultAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var assignedRunner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId, ownerId, requireOnline: false, cancellationToken: cancellationToken);
        var hostedRunner = await dbContext.HostedRunners.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunnerId == assignedRunner.Id && x.UserId == ownerId, cancellationToken);
        if (hostedRunner == null)
            return GetRunnerPresenceResult(assignedRunner);

        if (hostedRunner.DeletedAt != null || hostedRunner.Status is
            HostedRunnerStatuses.Disabled or HostedRunnerStatuses.Deleted or HostedRunnerStatuses.Deleting)
            return ManagedRunnerBootstrapResult.Unavailable(
                "The assigned Hosted Runner is no longer eligible for App execution.");

        if (hostedRunner.Status == HostedRunnerStatuses.Failed)
            return CreateHostedRunnerFailureResult(hostedRunner, "The assigned Hosted Runner failed to provision.");

        if (hostedRunner.Status is HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning or HostedRunnerStatuses.Reprovisioning)
            return await GetInProgressResultAsync(appId, hostedRunner, cancellationToken);

        if (hostedRunner.Status != HostedRunnerStatuses.Running)
            return ManagedRunnerBootstrapResult.Unavailable(
                "The assigned Hosted Runner is not available for App execution.");

        RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(assignedRunner);
        return GetRunnerPresenceResult(assignedRunner);
    }

    private ManagedRunnerBootstrapResult GetRunnerPresenceResult(Runner runner)
        => RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow)
            ? ManagedRunnerBootstrapResult.Ready(runner.Id)
            : ManagedRunnerBootstrapResult.Unavailable("The assigned Runner is offline.");

    private static ManagedRunnerBootstrapResult CreateHostedRunnerFailureResult(HostedRunner runner, string prefix)
        => ManagedRunnerBootstrapResult.ProvisionFailed(
            string.IsNullOrWhiteSpace(runner.LastError) ? prefix : $"{prefix} {runner.LastError}");

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
        => applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = appId,
            Level = ApplicationEventLevels.Info,
            Category = ApplicationEventCategories.Deployment,
            EventType = eventType,
            Message = message
        }, cancellationToken);
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
