using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

/// <summary>Authenticated Stage 4B2 relay for trusted Hosted container operations.</summary>
public sealed class HostContainerRelayService(
    HostContainerRelayStateResolver stateResolver,
    IHostContainerTransport transport,
    ILogger<HostContainerRelayService> logger)
{
    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Scope is null)
        {
            return Failure("invalid_request", "A host-container request and scope are required.");
        }

        var validationFailure = HostContainerRelayRequestValidator.ValidateCaller(caller);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var resolution = await stateResolver.ResolveAsync(caller, request, cancellationToken);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var state = resolution.State!;
        var activeResources = state.Hosted.SpecificationSnapshot.Resources;
        var requiresContainerLimits = RequiresContainerLimits(request.Operation);
        var forwarded = request with
        {
            Scope = new HostContainerScope(
                state.Hosted.Id,
                state.HostId,
                state.Target.WebAppId,
                state.Target.Id,
                request.Scope.DeploymentAttemptId),
            Resources = GetServerResources(request.Operation),
            SharedMemoryMb = requiresContainerLimits ? activeResources.SharedMemoryMb : null,
            DockerLogMaxSizeMb = requiresContainerLimits ? activeResources.DockerLogMaxSizeMb : null,
            DockerLogMaxFiles = requiresContainerLimits ? activeResources.DockerLogMaxFiles : null,
            HostPort = 0,
            Environment = new Dictionary<string, string>(request.Environment, StringComparer.Ordinal)
        };

        return await SendAsync(state, forwarded, cancellationToken);
    }

    private async Task<HostContainerExecutionResponse> SendAsync(
        HostContainerRelayState state,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.ExecuteAsync(state.Infrastructure.ConnectionId!, request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Host-container relay failed for host {RunnerHostId}", state.HostId);
            return Failure("infrastructure_transport_failure", "The Infrastructure Runner transport failed.");
        }
    }

    private static HostContainerResourceRef? GetServerResources(string operation) =>
        operation is HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.CreateApplicationContainer
            ? new HostContainerResourceRef(
                HostContainerResourceKinds.Source,
                HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData)
            : null;

    private static bool RequiresContainerLimits(string operation) =>
        operation is HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.CreateApplicationContainer;

    private static HostContainerExecutionResponse Failure(string code, string message) =>
        new() { ErrorCode = code, Error = message };
}
