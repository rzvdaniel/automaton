using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Revalidates queued machine Action ownership immediately before dispatch.</summary>
internal sealed class MachineActionAssignmentRevalidator(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization)
{
    public async Task<HostContainerExecutionResponse?> RevalidateAsync(
        Guid apiKeyId,
        Guid runnerId,
        Guid actorId,
        Guid operationId,
        AppActionCommand command,
        CancellationToken token)
    {
        if (command.Kind != "execute") return null;
        if (!TryReadRequest(command, out var request))
            return Failure("action_request_invalid", "The queued Action request is invalid.");
        var runner = await FindMachineRunnerAsync(apiKeyId, runnerId, token);
        if (runner is null) return null;

        return await ValidateActionBindingsAsync(request!, operationId, runner, actorId, token);
    }

    private async Task<Runner?> FindMachineRunnerAsync(Guid apiKeyId, Guid runnerId, CancellationToken token)
    {
        var runner = await db.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId && item.ApiKeyId == apiKeyId, token);
        return runner is { Purpose: RunnerPurposes.Managed }
            && RunnerApplicationProfiles.TryNormalize(runner.ApplicationProfile, out var profile)
            && profile == RunnerApplicationProfiles.MachineManagement ? runner : null;
    }

    private async Task<HostContainerExecutionResponse?> ValidateActionBindingsAsync(
        ExecuteAppActionModuleRequest request,
        Guid operationId,
        Runner runner,
        Guid actorId,
        CancellationToken token)
    {
        if (HasMissingBinding(request, operationId))
            return Failure("action_binding_missing", "Machine Actions require their durable execution, app and target binding.");
        if (!RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.WorkflowExecution, runner.Purpose))
            return Failure("action_capability_missing", "The Machine Management Runner is not enabled for Action execution.");
        var targetFailure = await ValidateTargetAndExecutionAsync(request, operationId, runner.Id, token);
        if (targetFailure is not null) return targetFailure;
        return await ValidateActorAssignmentAsync(request.AppId, runner.Id, actorId, token);
    }

    private async Task<HostContainerExecutionResponse?> ValidateTargetAndExecutionAsync(
        ExecuteAppActionModuleRequest request,
        Guid operationId,
        Guid runnerId,
        CancellationToken token)
    {
        var target = await db.AppDeploymentTargets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.DeploymentTargetId && item.WebAppId == request.AppId, token);
        if (target is null || target.ExecutionRunnerId != runnerId)
            return Failure("app_assignment_mismatch", "The Action target is no longer assigned to this Machine Management Runner.");
        var executionIsActive = await db.AppModuleExecutions.AsNoTracking()
            .AnyAsync(item => item.Id == operationId && item.AppId == request.AppId
                && item.DeploymentTargetId == request.DeploymentTargetId && item.RunnerId == runnerId
                && item.ModuleType == AppModuleTypes.Action && item.ModuleKey == request.ActionKey
                && item.CompletedAtUtc == null, token);
        return executionIsActive ? null
            : Failure("action_execution_stale", "The Action execution is no longer active for this target.");
    }

    private async Task<HostContainerExecutionResponse?> ValidateActorAssignmentAsync(
        Guid appId,
        Guid runnerId,
        Guid actorId,
        CancellationToken token)
    {
        if (actorId != Guid.Empty && !await authorization.CanExecuteAppAsync(appId, actorId, token))
            return Failure("workspace_access_revoked", "The Action actor no longer has permission to execute in this workspace.");
        return await IsWorkspaceAssignedAsync(appId, runnerId, actorId, token)
            ? null
            : Failure("workspace_assignment_missing", "The workspace is no longer assigned to this Machine Management Runner.");
    }

    private static bool HasMissingBinding(ExecuteAppActionModuleRequest request, Guid operationId)
        => request.ExecutionId != operationId || request.AppId == Guid.Empty
            || request.DeploymentTargetId == Guid.Empty || string.IsNullOrWhiteSpace(request.ActionKey);

    private static bool TryReadRequest(AppActionCommand command, out ExecuteAppActionModuleRequest? request)
    {
        try
        {
            request = command.Read<ExecuteAppActionModuleRequest>();
            return true;
        }
        catch (JsonException)
        {
            request = null;
            return false;
        }
    }

    private async Task<bool> IsWorkspaceAssignedAsync(Guid appId, Guid runnerId, Guid actorId, CancellationToken token)
    {
        var projectId = await db.WebApps.AsNoTracking().Where(app => app.Id == appId)
            .Select(app => app.ProjectId).SingleOrDefaultAsync(token);
        if (projectId is Guid assignedProjectId)
            return await db.ProjectRunners.AnyAsync(link => link.ProjectId == assignedProjectId && link.RunnerId == runnerId, token);
        return actorId != Guid.Empty
            && await db.WebAppUsers.AnyAsync(link => link.WebAppId == appId && link.UserId == actorId, token)
            && await db.RunnerUsers.AnyAsync(link => link.RunnerId == runnerId && link.UserId == actorId, token);
    }

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };

    public static RunnerExecutionResult CreateFailedResult(Guid executionId, HostContainerExecutionResponse failure)
        => new()
        {
            Outcome = "Failed", Started = DateTime.UtcNow, Finished = DateTime.UtcNow,
            Error = failure.Error,
            ActionResult = JsonSerializer.SerializeToElement(new AppActionExecutionResult
            {
                ExecutionId = executionId,
                Status = AppModuleExecutionStatuses.RunnerUnavailable,
                ErrorCode = AppModuleExecutionErrorCodes.RunnerUnavailable,
                FailureSummary = failure.Error,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            })
        };
}
