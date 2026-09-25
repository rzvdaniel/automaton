using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostContainerRelayStateResolver(
    ApplicationDbContext db,
    ApiInstanceIdentityService apiInstanceIdentityService)
{
    internal async Task<HostContainerRelayResolution> ResolveAsync(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var runnerLookup = await ResolveHostedRunnerAsync(caller, request, cancellationToken);
        if (runnerLookup.Failure is not null)
        {
            return HostContainerRelayResolution.Rejected(runnerLookup.Failure);
        }

        var binding = runnerLookup.Value!;
        var requestFailure = HostContainerRelayRequestValidator.ValidateOperation(request);
        if (requestFailure is not null)
        {
            return HostContainerRelayResolution.Rejected(requestFailure);
        }

        var targetLookup = await ResolveTargetAsync(caller, request, binding, cancellationToken);
        if (targetLookup.Failure is not null)
        {
            return HostContainerRelayResolution.Rejected(targetLookup.Failure);
        }

        var infrastructureLookup = await ResolveInfrastructureRunnerAsync(binding, cancellationToken);
        if (infrastructureLookup.Failure is not null)
        {
            return HostContainerRelayResolution.Rejected(infrastructureLookup.Failure);
        }

        return HostContainerRelayResolution.Resolved(new HostContainerRelayState(
            binding.HostedRunner,
            binding.HostId,
            targetLookup.Value!,
            infrastructureLookup.Value!));
    }

    private async Task<HostContainerRelayLookup<HostedRunnerBinding>> ResolveHostedRunnerAsync(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var persistedRunner = await db.Runners.SingleOrDefaultAsync(
            runner => runner.Id == caller.Id,
            cancellationToken);
        if (persistedRunner is null || !string.Equals(
                persistedRunner.ConnectionId,
                caller.ConnectionId,
                StringComparison.Ordinal))
        {
            return HostContainerRelayLookup<HostedRunnerBinding>.Rejected(
                Failure("caller_not_managed_runner", "The caller connection is not the persisted Runner connection."));
        }

        if (persistedRunner.Purpose != RunnerPurposes.Managed)
        {
            return HostContainerRelayLookup<HostedRunnerBinding>.Rejected(
                Failure("caller_not_managed_runner", "The caller is not a Managed Hosted Runner."));
        }

        if (!IsStandardProfile(persistedRunner.ApplicationProfile))
        {
            return HostContainerRelayLookup<HostedRunnerBinding>.Rejected(
                Failure("caller_not_managed_runner", "The persisted Runner application profile is not Standard."));
        }

        var hostedRunner = await db.HostedRunners
            .Include(hosted => hosted.RunnerHost)
            .SingleOrDefaultAsync(
                hosted => hosted.Id == caller.HostedRunnerId
                    && hosted.RunnerId == caller.Id
                    && hosted.DeletedAt == null,
                cancellationToken);
        if (hostedRunner?.RunnerHostId is not Guid hostId || hostedRunner.RunnerHost is null)
        {
            return HostContainerRelayLookup<HostedRunnerBinding>.Rejected(
                Failure("hosted_runner_binding_mismatch", "The Hosted Runner is not bound to a Runner Host."));
        }

        var bindingFailure = ValidateRequestedBinding(caller, request.Scope, hostedRunner, hostId);
        return bindingFailure is null
            ? HostContainerRelayLookup<HostedRunnerBinding>.Resolved(new HostedRunnerBinding(hostedRunner, hostId))
            : HostContainerRelayLookup<HostedRunnerBinding>.Rejected(bindingFailure);
    }

    private async Task<HostContainerRelayLookup<AppDeploymentTarget>> ResolveTargetAsync(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        HostedRunnerBinding binding,
        CancellationToken cancellationToken)
    {
        var target = await db.AppDeploymentTargets.SingleOrDefaultAsync(
            deploymentTarget => deploymentTarget.Id == request.Scope.DeploymentTargetId
                && deploymentTarget.WebAppId == request.Scope.AppId,
            cancellationToken);
        if (target is null)
        {
            return HostContainerRelayLookup<AppDeploymentTarget>.Rejected(
                Failure("deployment_target_mismatch", "The deployment target was not found for the application."));
        }

        var targetFailure = ValidateTarget(caller, request, target);
        return targetFailure is null
            ? HostContainerRelayLookup<AppDeploymentTarget>.Resolved(target)
            : HostContainerRelayLookup<AppDeploymentTarget>.Rejected(targetFailure);
    }

    private async Task<HostContainerRelayLookup<Runner>> ResolveInfrastructureRunnerAsync(
        HostedRunnerBinding binding,
        CancellationToken cancellationToken)
    {
        var infrastructure = await db.Runners.SingleOrDefaultAsync(
            runner => runner.Id == binding.HostedRunner.RunnerHost!.InfrastructureRunnerId,
            cancellationToken);
        if (infrastructure is null || infrastructure.Purpose != RunnerPurposes.Infrastructure)
        {
            return HostContainerRelayLookup<Runner>.Rejected(
                Failure("infrastructure_runner_missing", "No same-host Infrastructure Runner is available."));
        }

        var infrastructureFailure = ValidateInfrastructureRunner(infrastructure);
        return infrastructureFailure is null
            ? HostContainerRelayLookup<Runner>.Resolved(infrastructure)
            : HostContainerRelayLookup<Runner>.Rejected(infrastructureFailure);
    }

    private static bool IsStandardProfile(string? applicationProfile) =>
        RunnerApplicationProfiles.TryNormalize(applicationProfile, out var profile)
        && string.Equals(profile, RunnerApplicationProfiles.Standard, StringComparison.Ordinal);

    private static HostContainerExecutionResponse? ValidateRequestedBinding(
        RunnerDetails caller,
        HostContainerScope scope,
        HostedRunner hostedRunner,
        Guid hostId)
    {
        if (caller.RunnerHostId is not Guid callerHostId || callerHostId != hostId)
        {
            return Failure("runner_host_binding_mismatch", "The caller Runner Host does not match persisted state.");
        }

        if (scope.HostedRunnerId != Guid.Empty && scope.HostedRunnerId != hostedRunner.Id)
        {
            return Failure("hosted_runner_binding_mismatch", "The requested Hosted Runner does not match persisted state.");
        }

        return scope.RunnerHostId != Guid.Empty && scope.RunnerHostId != hostId
            ? Failure("runner_host_binding_mismatch", "The requested Runner Host does not match persisted state.")
            : null;
    }

    private static HostContainerExecutionResponse? ValidateTarget(
        RunnerDetails caller,
        HostContainerExecutionRequest request,
        AppDeploymentTarget target)
    {
        if (target.ExecutionRunnerId != caller.Id)
        {
            return Failure("app_assignment_mismatch", "The target is not assigned to the authenticated Hosted Runner.");
        }

        return request.Operation == HostContainerOperationKinds.RunBuild
            && target.ActiveDeploymentExecutionAttemptId != request.Scope.DeploymentAttemptId
            ? Failure("deployment_attempt_mismatch", "The build attempt is not the active deployment attempt.")
            : null;
    }

    private HostContainerExecutionResponse? ValidateInfrastructureRunner(Runner infrastructure)
    {
        if (!RunnerPresenceEvaluator.IsOnline(
                infrastructure.ConnectionId,
                infrastructure.LastHeartbeatAtUtc,
                DateTime.UtcNow))
        {
            return Failure("infrastructure_runner_offline", "The same-host Infrastructure Runner is offline.");
        }

        if (!int.TryParse(infrastructure.ApiCompatibilityVersion, out var protocol)
            || protocol < int.Parse(RunnerApiCompatibilityVersions.HostContainerExecution))
        {
            return Failure("host_container_protocol_unsupported", "The Infrastructure Runner does not support protocol 4.");
        }

        if (!RunnerCapabilities.Includes(
                infrastructure.CapabilitiesJson,
                RunnerCapabilities.HostContainerExecution,
                infrastructure.Purpose))
        {
            return Failure("host_container_capability_missing", "The Infrastructure Runner lacks HostContainerExecution capability.");
        }

        return RuntimeTunnelService.IsCurrentApiInstance(
            infrastructure.ApiInstanceId,
            apiInstanceIdentityService.ApiInstanceId)
            ? null
            : Failure("infrastructure_runner_wrong_api_instance", "The Infrastructure Runner is connected to another API instance.");
    }

    private static HostContainerExecutionResponse Failure(string code, string message) =>
        new() { ErrorCode = code, Error = message };
}

internal sealed record HostedRunnerBinding(HostedRunner HostedRunner, Guid HostId);

internal sealed record HostContainerRelayState(
    HostedRunner Hosted,
    Guid HostId,
    AppDeploymentTarget Target,
    Runner Infrastructure);

internal sealed record HostContainerRelayResolution(
    HostContainerRelayState? State,
    HostContainerExecutionResponse? Failure)
{
    public static HostContainerRelayResolution Resolved(HostContainerRelayState state) => new(state, null);
    public static HostContainerRelayResolution Rejected(HostContainerExecutionResponse failure) => new(null, failure);
}

internal sealed record HostContainerRelayLookup<T>(T? Value, HostContainerExecutionResponse? Failure)
    where T : class
{
    public static HostContainerRelayLookup<T> Resolved(T value) => new(value, null);
    public static HostContainerRelayLookup<T> Rejected(HostContainerExecutionResponse failure) => new(null, failure);
}
