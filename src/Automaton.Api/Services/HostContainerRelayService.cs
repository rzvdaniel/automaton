using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Authenticated Stage 4B2 relay for trusted Hosted build operations.</summary>
public sealed class HostContainerRelayService(
    ApplicationDbContext db,
    IHostContainerTransport transport,
    ILogger<HostContainerRelayService> logger,
    ApiInstanceIdentityService apiInstanceIdentityService)
{
    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Scope is null)
            return Failure("invalid_request", "A host-container request and scope are required.");
        if (caller.Purpose != RunnerPurposes.Managed || caller.HostedRunnerId is null)
            return Failure("caller_not_managed_runner", "The caller is not a Managed Hosted Runner.");

        var persistedRunner = await db.Runners.SingleOrDefaultAsync(x => x.Id == caller.Id, cancellationToken);
        if (persistedRunner is null || !string.Equals(persistedRunner.ConnectionId, caller.ConnectionId, StringComparison.Ordinal)
            || persistedRunner.Purpose != RunnerPurposes.Managed
            || !string.Equals(persistedRunner.ApplicationProfile, RunnerApplicationProfiles.Standard, StringComparison.OrdinalIgnoreCase))
            return Failure("caller_not_managed_runner", "The caller connection is not the persisted Runner connection.");

        var hosted = await db.HostedRunners.Include(x => x.RunnerHost)
            .SingleOrDefaultAsync(x => x.Id == caller.HostedRunnerId && x.RunnerId == caller.Id && x.DeletedAt == null, cancellationToken);
        if (hosted?.RunnerHostId is not Guid hostId || hosted.RunnerHost is null)
            return Failure("hosted_runner_binding_mismatch", "The Hosted Runner is not bound to a Runner Host.");
        if (caller.RunnerHostId is not Guid callerHostId || callerHostId != hostId)
            return Failure("runner_host_binding_mismatch", "The caller Runner Host does not match persisted state.");
        if (request.Scope.HostedRunnerId != Guid.Empty && request.Scope.HostedRunnerId != hosted.Id)
            return Failure("hosted_runner_binding_mismatch", "The requested Hosted Runner does not match persisted state.");
        if (request.Scope.RunnerHostId != Guid.Empty && request.Scope.RunnerHostId != hostId)
            return Failure("runner_host_binding_mismatch", "The requested Runner Host does not match persisted state.");
        if (request.Operation is not (HostContainerOperationKinds.ResolveImage or HostContainerOperationKinds.RunBuild))
            return Failure("unsupported_operation", "Stage 4B2b supports ResolveImage and RunBuild only.");
        if (string.IsNullOrWhiteSpace(request.RequestedImage))
            return Failure("invalid_request", "ResolveImage requires an image reference.");
        if (request.Operation == HostContainerOperationKinds.ResolveImage
            && (request.Resources is not null || request.ContainerId is not null || request.Command is not null
                || request.HostPort != 0 || request.ContainerPort != 0 || request.Environment is null || request.Environment.Count != 0))
            return Failure("unsupported_operation", "ResolveImage does not accept container execution inputs.");
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && (request.Command is null || request.Resources is not null || request.ContainerId is not null
                || request.HostPort != 0 || request.ContainerPort != 0))
            return Failure("invalid_request", "RunBuild requires a command and server-derived resources.");
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && (request.TimeoutSeconds is < 1 or > 86_400))
            return Failure("invalid_request", "The build timeout must be between 1 and 86400 seconds.");
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && (request.Environment is null || request.Command!.Arguments is null || request.Command.Environment is null
                || string.IsNullOrWhiteSpace(request.Command.Executable)))
            return Failure("invalid_request", "RunBuild contains an invalid command or environment.");
        if (request.Operation == HostContainerOperationKinds.RunBuild)
        {
            var contractErrors = ApplicationContractValidator.Validate(new ApplicationContract
            {
                Build = new ApplicationBuildSpec
                {
                    Image = request.RequestedImage,
                    Command = request.Command,
                    TimeoutSeconds = request.TimeoutSeconds
                }
            });
            if (contractErrors.Count > 0)
                return Failure("invalid_request", string.Join("; ", contractErrors));
        }
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && (request.RequestedImage!.Any(char.IsWhiteSpace) || request.RequestedImage.Contains('\0')
                || request.RequestedImage.StartsWith("-", StringComparison.Ordinal)))
            return Failure("invalid_request", "RunBuild contains an invalid image reference.");
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && request.Scope.DeploymentAttemptId == Guid.Empty)
            return Failure("invalid_request", "RunBuild requires a server-issued deployment attempt.");

        var target = await db.AppDeploymentTargets.SingleOrDefaultAsync(
            x => x.Id == request.Scope.DeploymentTargetId && x.WebAppId == request.Scope.AppId,
            cancellationToken);
        if (target is null)
            return Failure("deployment_target_mismatch", "The deployment target was not found for the application.");
        if (target.ExecutionRunnerId != caller.Id)
            return Failure("app_assignment_mismatch", "The target is not assigned to the authenticated Hosted Runner.");
        if (request.Operation == HostContainerOperationKinds.RunBuild
            && target.ActiveDeploymentExecutionAttemptId != request.Scope.DeploymentAttemptId)
            return Failure("deployment_attempt_mismatch", "The build attempt is not the active deployment attempt.");

        var infrastructure = await db.Runners.SingleOrDefaultAsync(
            x => x.Id == hosted.RunnerHost.InfrastructureRunnerId,
            cancellationToken);
        if (infrastructure is null || infrastructure.Purpose != RunnerPurposes.Infrastructure)
            return Failure("infrastructure_runner_missing", "No same-host Infrastructure Runner is available.");
        if (!RunnerPresenceEvaluator.IsOnline(infrastructure.ConnectionId, infrastructure.LastHeartbeatAtUtc, DateTime.UtcNow))
            return Failure("infrastructure_runner_offline", "The same-host Infrastructure Runner is offline.");
        if (!int.TryParse(infrastructure.ApiCompatibilityVersion, out var protocol)
            || protocol < int.Parse(RunnerApiCompatibilityVersions.HostContainerExecution))
            return Failure("host_container_protocol_unsupported", "The Infrastructure Runner does not support protocol 4.");
        if (!RunnerCapabilities.Includes(infrastructure.CapabilitiesJson, RunnerCapabilities.HostContainerExecution, infrastructure.Purpose))
            return Failure("host_container_capability_missing", "The Infrastructure Runner lacks HostContainerExecution capability.");
        if (!RuntimeTunnelService.IsCurrentApiInstance(infrastructure.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId))
            return Failure("infrastructure_runner_wrong_api_instance", "The Infrastructure Runner is connected to another API instance.");

        var forwarded = request with
        {
            Scope = new HostContainerScope(hosted.Id, hostId, target.WebAppId, target.Id, request.Scope.DeploymentAttemptId),
            Resources = request.Operation == HostContainerOperationKinds.RunBuild
                ? new HostContainerResourceRef(
                    HostContainerResourceKinds.Source,
                    HostContainerResourceKinds.Artifact,
                    HostContainerResourceKinds.ApplicationData)
                : null,
            ContainerId = null,
            Command = request.Operation == HostContainerOperationKinds.RunBuild ? request.Command : null,
            HostPort = 0,
            ContainerPort = 0,
            Environment = request.Operation == HostContainerOperationKinds.RunBuild
                ? new Dictionary<string, string>(request.Environment, StringComparer.Ordinal)
                : new Dictionary<string, string>()
        };
        try
        {
            return await transport.ExecuteAsync(infrastructure.ConnectionId!, forwarded, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Host-container relay failed for host {RunnerHostId}", hostId);
            return Failure("infrastructure_transport_failure", "The Infrastructure Runner transport failed.");
        }
    }

    private static HostContainerExecutionResponse Failure(string code, string message) => new() { ErrorCode = code, Error = message };
}
