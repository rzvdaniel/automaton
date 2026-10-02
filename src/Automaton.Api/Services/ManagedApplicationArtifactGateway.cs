using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Queues owner-scoped artifact replacement on the assigned Runner's management queue.</summary>
public sealed class ManagedApplicationArtifactGateway(ApplicationDbContext db, RunnerExecutionOperationService operations)
    : IManagedApplicationArtifactCommands
{
    public async Task<SynchronizeApplicationManagedArtifactsResult> SynchronizeAsync(Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, Guid? deploymentOperationId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(runnerId, ownerId, request, cancellationToken);
        var id = deploymentOperationId.HasValue
            ? OperationIdentity(deploymentOperationId.Value, runnerId) : Guid.NewGuid();
        await operations.QueueManagedArtifactsAsync(id, runnerId, ownerId, request, cancellationToken);
        var result = await operations.WaitAsync(id, cancellationToken);
        if (result.Outcome != "Succeeded" || result.ManagedArtifactsResult is null || !result.ManagedArtifactsResult.Success)
            throw new ManagedApplicationArtifactFailureException(
                result.Error ?? result.ManagedArtifactsResult?.Error ?? $"Managed artifact synchronization ended with {result.Outcome}.");
        return result.ManagedArtifactsResult;
    }

    private async Task AuthorizeAsync(Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, CancellationToken token)
    {
        var target = await db.AppDeploymentTargets.AsNoTracking().Include(t => t.ExecutionRunner)
            .SingleOrDefaultAsync(t => t.Id == request.DeploymentTargetId && t.WebAppId == request.AppId, token);
        if (target?.ExecutionRunnerId != runnerId
            || !await db.WebAppUsers.AnyAsync(u => u.WebAppId == request.AppId && u.UserId == ownerId, token))
            throw new UnauthorizedAccessException("Managed artifacts do not belong to this owner and assigned Runner.");
        var runner = target.ExecutionRunner!;
        PrivilegedHostAssignmentGuard.EnsureApproved(target, runner);
        RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(runner);
        if (!runner.LastHeartbeatAtUtc.HasValue
            || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold)
            throw new InvalidOperationException("The assigned Runner is unavailable.");
    }

    internal static Guid OperationIdentity(Guid deploymentOperationId, Guid runnerId)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"managed-artifacts:{deploymentOperationId:N}:{runnerId:N}"))
            .AsSpan(0, 16));
}
