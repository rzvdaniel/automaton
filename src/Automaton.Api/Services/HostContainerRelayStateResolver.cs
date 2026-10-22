using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostContainerRelayStateResolver(
    ApplicationDbContext db)
{
    internal async Task<HostContainerRelayResolution> ResolveAsync(
        Guid apiKeyId,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken,
        bool serverPrepared = false)
    {
        var caller = await db.Runners.SingleOrDefaultAsync(runner => runner.ApiKeyId == apiKeyId, cancellationToken);
        if (caller is null || caller.Purpose != RunnerPurposes.Managed)
        {
            return HostContainerRelayResolution.Rejected(
                Failure("caller_not_managed_runner", "The API key is not assigned to a Managed Runner."));
        }

        if (!IsMachineManagementProfile(caller.ApplicationProfile))
            return HostContainerRelayResolution.Rejected(Failure("machine_runner_required",
                "Hosted container execution is retired. Assign a Management Runner to this app."));
        return await ResolveMachineManagementAsync(caller, request, cancellationToken, serverPrepared);
    }

    private async Task<HostContainerRelayResolution> ResolveMachineManagementAsync(
        Runner caller,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken,
        bool serverPrepared)
    {
        if (!RunnerCapabilities.Includes(caller.CapabilitiesJson,
                RunnerCapabilities.ManagedContainerExecution, caller.Purpose))
            return HostContainerRelayResolution.Rejected(
                Failure("machine_container_capability_missing", "The Machine Management Runner lacks typed container capability."));

        var bindingFailure = ValidateMachineBinding(caller, request.Scope, serverPrepared);
        if (bindingFailure is not null)
            return HostContainerRelayResolution.Rejected(bindingFailure);

        var requestFailure = HostContainerRelayRequestValidator.ValidateOperation(request, serverPrepared);
        if (requestFailure is not null)
            return HostContainerRelayResolution.Rejected(requestFailure);

        var target = await db.AppDeploymentTargets.SingleOrDefaultAsync(
            item => item.Id == request.Scope.DeploymentTargetId && item.WebAppId == request.Scope.AppId,
            cancellationToken);
        if (target is null || target.ExecutionRunnerId != caller.Id)
            return HostContainerRelayResolution.Rejected(
                Failure("app_assignment_mismatch", "The target is not assigned to this machine Runner."));
        if ((request.Operation is HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.ApplyComposeProject)
            && target.ActiveDeploymentExecutionAttemptId != request.Scope.DeploymentAttemptId)
            return HostContainerRelayResolution.Rejected(
                Failure("deployment_attempt_mismatch", "The build attempt is not the active deployment attempt."));

        var actorId = await ResolveMachineWorkspaceActorAsync(caller, target, cancellationToken);
        if (!actorId.HasValue)
            return HostContainerRelayResolution.Rejected(
                Failure("workspace_assignment_missing", "The app workspace is not assigned to this machine Runner."));

        return HostContainerRelayResolution.Resolved(new HostContainerRelayState(
            target, caller, actorId.Value));
    }

    private static HostContainerExecutionResponse? ValidateMachineBinding(
        Runner caller,
        HostContainerScope scope,
        bool serverPrepared)
    {
        var machineRunnerMismatch = serverPrepared
            ? scope.MachineRunnerId != caller.Id
            : scope.MachineRunnerId != Guid.Empty && scope.MachineRunnerId != caller.Id;
        return machineRunnerMismatch || scope.HostedRunnerId != Guid.Empty || scope.RunnerHostId != Guid.Empty
            ? Failure("machine_runner_binding_mismatch", "The requested machine scope does not match this Runner.")
            : null;
    }

    private async Task<Guid?> ResolveMachineWorkspaceActorAsync(
        Runner caller,
        AppDeploymentTarget target,
        CancellationToken cancellationToken)
    {
        var app = await db.WebApps.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == target.WebAppId, cancellationToken);
        if (app is null) return null;

        if (app.ProjectId is Guid projectId)
        {
            var project = await db.Projects.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
            if (project is null || !await db.ProjectRunners.AnyAsync(
                    item => item.ProjectId == projectId && item.RunnerId == caller.Id, cancellationToken))
                return null;
            return project.OwnerId;
        }

        return await db.WebAppUsers
            .Where(user => user.WebAppId == app.Id
                && db.RunnerUsers.Any(link => link.RunnerId == caller.Id && link.UserId == user.UserId))
            .Select(user => (Guid?)user.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsMachineManagementProfile(string? applicationProfile) =>
        RunnerApplicationProfiles.TryNormalize(applicationProfile, out var profile)
        && string.Equals(profile, RunnerApplicationProfiles.MachineManagement, StringComparison.Ordinal);

    private static HostContainerExecutionResponse Failure(string code, string message) =>
        new() { ErrorCode = code, Error = message };
}

internal sealed record HostContainerRelayState(
    AppDeploymentTarget Target,
    Runner MachineRunner,
    Guid OperationActorUserId);

internal sealed record HostContainerRelayResolution(
    HostContainerRelayState? State,
    HostContainerExecutionResponse? Failure)
{
    public static HostContainerRelayResolution Resolved(HostContainerRelayState state) => new(state, null);
    public static HostContainerRelayResolution Rejected(HostContainerExecutionResponse failure) => new(null, failure);
}
