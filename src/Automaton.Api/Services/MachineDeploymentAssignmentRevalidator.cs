using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Revalidates a queued Machine Management package before the Runner receives it.</summary>
internal sealed class MachineDeploymentAssignmentRevalidator(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization)
{
    public async Task<HostContainerExecutionResponse?> RevalidateAsync(
        Guid apiKeyId,
        Guid runnerId,
        Guid actorId,
        RunnerFileTransferCommand request,
        CancellationToken token)
    {
        if (!await IsMachineManagementRunnerAsync(apiKeyId, runnerId, token))
            return null;

        if (!TryReadBinding(request, out var binding))
            return Failure("deployment_binding_missing", "Machine deployments require a target, operation, and execution-attempt binding.");

        var target = await db.AppDeploymentTargets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == binding.TargetId && item.WebAppId == request.AppId, token);
        if (target is null || target.ExecutionRunnerId != runnerId)
            return Failure("app_assignment_mismatch", "The target is no longer assigned to this Machine Management Runner.");
        if (target.ActiveDeploymentOperationId != binding.OperationId
            || target.ActiveDeploymentExecutionAttemptId != binding.AttemptId)
            return Failure("deployment_attempt_mismatch", "The deployment is no longer the active operation and attempt for this target.");
        if (!await HasCurrentOperationAsync(request, actorId, target, binding, token))
            return Failure("deployment_operation_mismatch", "The durable deployment operation is no longer active for this target.");
        if (!await authorization.CanUpdateAppAsync(request.AppId, actorId, token))
            return Failure("workspace_access_revoked", "The deployment actor no longer has permission to update this workspace.");
        if (!await IsWorkspaceAssignedAsync(request.AppId, runnerId, actorId, token))
            return Failure("workspace_assignment_missing", "The workspace is no longer assigned to this Machine Management Runner.");
        return null;
    }

    private async Task<bool> IsMachineManagementRunnerAsync(Guid apiKeyId, Guid runnerId, CancellationToken token)
    {
        var runner = await db.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId && item.ApiKeyId == apiKeyId, token);
        return runner is not null
            && runner.Purpose == RunnerPurposes.Managed
            && RunnerApplicationProfiles.TryNormalize(runner.ApplicationProfile, out var profile)
            && profile == RunnerApplicationProfiles.MachineManagement;
    }

    private static bool TryReadBinding(RunnerFileTransferCommand request, out DeploymentBinding binding)
    {
        if (request.Kind == RunnerFileTransferKinds.DeployPackage
            && request.DeploymentTargetId is Guid targetId && targetId != Guid.Empty
            && request.DeploymentOperationId is Guid operationId && operationId != Guid.Empty
            && request.ExecutionAttemptId is Guid attemptId && attemptId != Guid.Empty)
        {
            binding = new(targetId, operationId, attemptId);
            return true;
        }

        binding = default;
        return false;
    }

    private Task<bool> HasCurrentOperationAsync(
        RunnerFileTransferCommand request,
        Guid actorId,
        AppDeploymentTarget target,
        DeploymentBinding binding,
        CancellationToken token)
        => db.DeploymentOperations.AsNoTracking().AnyAsync(operation =>
            operation.Id == binding.OperationId
            && operation.WebAppId == request.AppId
            && operation.OwnerUserId == actorId
            && operation.Status == DeploymentOperationStatuses.Running
            && operation.ExecutionAttemptId == binding.AttemptId
            && (operation.TargetSlug == null ? target.IsDefault : operation.TargetSlug == target.Slug), token);

    private async Task<bool> IsWorkspaceAssignedAsync(
        Guid appId,
        Guid runnerId,
        Guid actorId,
        CancellationToken token)
    {
        var projectId = await db.WebApps.AsNoTracking()
            .Where(app => app.Id == appId)
            .Select(app => app.ProjectId)
            .SingleOrDefaultAsync(token);
        if (projectId is Guid assignedProjectId)
            return await db.ProjectRunners.AnyAsync(link => link.ProjectId == assignedProjectId && link.RunnerId == runnerId, token);

        return await db.WebAppUsers.AnyAsync(link => link.WebAppId == appId && link.UserId == actorId, token)
            && await db.RunnerUsers.AnyAsync(link => link.RunnerId == runnerId && link.UserId == actorId, token);
    }

    private static HostContainerExecutionResponse Failure(string code, string message)
        => new() { ErrorCode = code, Error = message };

    private readonly record struct DeploymentBinding(Guid TargetId, Guid OperationId, Guid AttemptId);
}
