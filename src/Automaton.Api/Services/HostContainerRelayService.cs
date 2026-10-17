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
    IOptions<StorageTelemetryOptions> storageTelemetryOptions)
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
            var managedRequest = HostContainerRelayRequestPreparer.PrepareForMachine(request, state);
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
                or HostContainerOperationKinds.CreateApplicationContainer)
            || state.Hosted?.RunnerHost is { } host
                && StorageTelemetryPolicy.CanRunDockerStorageOperation(host, DateTime.UtcNow, options);
}

internal static class HostContainerRelayRequestPreparer
{
    public static HostContainerExecutionRequest Prepare(
        HostContainerExecutionRequest request,
        HostContainerRelayState state)
    {
        var requiresContainerLimits = request.Operation is HostContainerOperationKinds.RunBuild
            or HostContainerOperationKinds.CreateApplicationContainer;
        var resources = requiresContainerLimits
            ? new HostContainerResourceRef(HostContainerResourceKinds.Source,
                HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData)
            : null;
        var activeResources = state.Hosted!.SpecificationSnapshot.Resources;
        return request with
        {
            Scope = new HostContainerScope(state.Hosted.Id, state.HostId, state.Target.WebAppId,
                state.Target.Id, request.Scope.DeploymentAttemptId),
            Resources = resources,
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
        HostContainerRelayState state)
    {
        var requiresContainerLimits = request.Operation is HostContainerOperationKinds.RunBuild
            or HostContainerOperationKinds.CreateApplicationContainer;
        return request with
        {
            Scope = new HostContainerScope(
                Guid.Empty, Guid.Empty, state.Target.WebAppId, state.Target.Id,
                request.Scope.DeploymentAttemptId, state.MachineRunner!.Id),
            Resources = requiresContainerLimits
                ? new HostContainerResourceRef(HostContainerResourceKinds.Source,
                    HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData)
                : null,
            SharedMemoryMb = requiresContainerLimits ? 64 : null,
            DockerLogMaxSizeMb = requiresContainerLimits ? 20 : null,
            DockerLogMaxFiles = requiresContainerLimits ? 5 : null,
            HostPort = 0,
            Environment = request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }
}
