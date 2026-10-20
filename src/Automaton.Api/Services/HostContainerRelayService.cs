using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Authenticated Stage 4B2 relay for trusted Hosted container operations.</summary>
public sealed class HostContainerRelayService(
    HostContainerRelayStateResolver stateResolver,
    HostContainerExecutionOperationService operations,
    RunnerExecutionOperationService runnerOperations,
    IOptions<StorageTelemetryOptions> storageTelemetryOptions,
    IOptions<MachineContainerResourceOptions> machineContainerResources)
{
    public async Task<HostContainerExecutionOperationResponse> SubmitAsync(
        Guid apiKeyId,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Scope is null || request.OperationId == Guid.Empty)
        {
            return CompletedFailure(request?.OperationId ?? Guid.Empty,
                "invalid_request", "A host-container request, operation ID, and scope are required.");
        }

        var resolution = await stateResolver.ResolveAsync(apiKeyId, request, cancellationToken);
        if (resolution.Failure is not null)
        {
            return Completed(request.OperationId, resolution.Failure);
        }

        var state = resolution.State!;
        if (state.MachineRunner is not null)
        {
            if ((HostContainerRelayRequestPreparer.RequiresContainerLimits(request.Operation)
                    || HostContainerRelayRequestPreparer.RequiresDiskAdmission(request.Operation))
                && !machineContainerResources.Value.IsValid())
                return Completed(request.OperationId, Failure(
                    "machine_resource_policy_unavailable",
                    "Machine container execution is paused because valid CPU, memory, PID, and minimum free-disk settings are not configured."));

            var managedRequest = HostContainerRelayRequestPreparer.PrepareForMachine(
                request, state, machineContainerResources.Value);
            await runnerOperations.QueueHostContainerAsync(
                managedRequest.OperationId,
                state.MachineRunner.Id,
                state.OperationActorUserId,
                managedRequest,
                cancellationToken);
            return new(managedRequest.OperationId, false, null);
        }

        if (!HostContainerRelayAdmission.CanRun(request, state, storageTelemetryOptions.Value))
        {
            return Completed(request.OperationId, Failure(
                "host_storage_reserve_unavailable",
                "Docker image operations are paused until the Runner Host has fresh storage telemetry and enough free space to preserve its reserve."));
        }

        var forwarded = HostContainerRelayRequestPreparer.Prepare(request, state);

        return await operations.QueueAsync(
            forwarded,
            state.HostId,
            state.Infrastructure.Id,
            cancellationToken);
    }

    private static HostContainerExecutionResponse Failure(string code, string message) =>
        new() { ErrorCode = code, Error = message };

    private static HostContainerExecutionOperationResponse Completed(
        Guid operationId,
        HostContainerExecutionResponse result)
        => new(operationId, true, result);

    private static HostContainerExecutionOperationResponse CompletedFailure(
        Guid operationId,
        string code,
        string message)
        => new(operationId, true, Failure(code, message));
}

internal static class HostContainerRelayAdmission
{
    public static bool CanRun(
        HostContainerExecutionRequest request,
        HostContainerRelayState state,
        StorageTelemetryOptions options)
        => state.MachineRunner is not null
            || request.Operation is not (HostContainerOperationKinds.ResolveImage
                or HostContainerOperationKinds.RunBuild
                or HostContainerOperationKinds.CreateApplicationContainer
                or HostContainerOperationKinds.ApplyComposeProject)
            || state.Hosted?.RunnerHost is { } host
                && StorageTelemetryPolicy.CanRunDockerStorageOperation(host, DateTime.UtcNow, options);
}

internal static class HostContainerRelayRequestPreparer
{
    public static HostContainerExecutionRequest Prepare(
        HostContainerExecutionRequest request,
        HostContainerRelayState state)
    {
        var requiresContainerLimits = RequiresContainerLimits(request.Operation);
        var resources = requiresContainerLimits
            ? new HostContainerResourceRef(HostContainerResourceKinds.Source,
                HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData)
            : null;
        var activeResources = state.Hosted!.SpecificationSnapshot.Resources;
        var limits = requiresContainerLimits
            ? new HostContainerResourceLimits(activeResources.CpuLimit, activeResources.MemoryLimitMb, activeResources.PidsLimit)
            : null;
        return request with
        {
            Scope = new HostContainerScope(state.Hosted.Id, state.HostId, state.Target.WebAppId,
                state.Target.Id, request.Scope.DeploymentAttemptId),
            Resources = resources,
            ContainerLimits = limits,
            SharedMemoryMb = requiresContainerLimits ? activeResources.SharedMemoryMb : null,
            DockerLogMaxSizeMb = requiresContainerLimits ? activeResources.DockerLogMaxSizeMb : null,
            DockerLogMaxFiles = requiresContainerLimits ? activeResources.DockerLogMaxFiles : null,
            HostPort = 0,
            Environment = request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }

    public static HostContainerExecutionRequest PrepareForMachine(
        HostContainerExecutionRequest request,
        HostContainerRelayState state,
        MachineContainerResourceOptions resourceOptions)
    {
        var requiresContainerLimits = RequiresContainerLimits(request.Operation);
        return request with
        {
            Scope = new HostContainerScope(
                Guid.Empty, Guid.Empty, state.Target.WebAppId, state.Target.Id,
                request.Scope.DeploymentAttemptId, state.MachineRunner!.Id),
            Resources = requiresContainerLimits
                ? new HostContainerResourceRef(HostContainerResourceKinds.Source,
                    HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData)
                : null,
            ContainerLimits = requiresContainerLimits
                ? new HostContainerResourceLimits(resourceOptions.CpuLimitCores,
                    resourceOptions.MemoryLimitMb, resourceOptions.PidsLimit)
                : null,
            SharedMemoryMb = requiresContainerLimits ? resourceOptions.SharedMemoryMb : null,
            DockerLogMaxSizeMb = requiresContainerLimits ? resourceOptions.DockerLogMaxSizeMb : null,
            DockerLogMaxFiles = requiresContainerLimits ? resourceOptions.DockerLogMaxFiles : null,
            MinimumFreeDiskMb = RequiresDiskAdmission(request.Operation) ? resourceOptions.MinimumFreeDiskMb : null,
            HostPort = 0,
            Environment = request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }

    public static bool RequiresContainerLimits(string operation)
        => operation is HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.CreateApplicationContainer
            or HostContainerOperationKinds.ApplyComposeProject;

    public static bool RequiresDiskAdmission(string operation)
        => operation is HostContainerOperationKinds.ResolveImage or HostContainerOperationKinds.RunBuild
            or HostContainerOperationKinds.CreateApplicationContainer or HostContainerOperationKinds.ApplyComposeProject
            or HostContainerOperationKinds.PrepareActionDependencies or HostContainerOperationKinds.ExecuteAction;
}
