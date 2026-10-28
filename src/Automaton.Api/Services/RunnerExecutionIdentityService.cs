using Automaton.Api.Data;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Requires an enrolled Machine Management Runner key for execution.</summary>
public sealed class RunnerExecutionIdentityService(ApplicationDbContext db, ResourceAuthorizationService authorization)
{
    public async Task<HostContainerExecutionResponse?> RevalidateHostContainerAssignmentAsync(
        Guid apiKeyId,
        HostContainerExecutionRequest request,
        CancellationToken token)
    {
        var resolution = await new HostContainerRelayStateResolver(db)
            .ResolveAsync(apiKeyId, request, token, serverPrepared: true);
        return resolution.Failure;
    }

    public async Task<HostContainerExecutionResponse?> RevalidateMachineDeploymentAssignmentAsync(
        Guid apiKeyId,
        Guid runnerId,
        Guid actorId,
        RunnerFileTransferCommand request,
        CancellationToken token)
        => await new MachineDeploymentAssignmentRevalidator(db, authorization)
            .RevalidateAsync(apiKeyId, runnerId, actorId, request, token);

    public async Task<HostContainerExecutionResponse?> RevalidateMachineActionAssignmentAsync(
        Guid apiKeyId,
        Guid runnerId,
        Guid actorId,
        Guid operationId,
        AppActionCommand command,
        CancellationToken token)
        => await new MachineActionAssignmentRevalidator(db, authorization)
            .RevalidateAsync(apiKeyId, runnerId, actorId, operationId, command, token);

    public async Task EnsureAsync(Guid keyId, Guid runnerId, Guid authenticatedUserId, CancellationToken token)
    {
        if (keyId == Guid.Empty || runnerId == Guid.Empty)
            throw new UnauthorizedAccessException("Runner API-key authentication and identity are required.");
        var runner = await db.Runners.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runnerId, token)
            ?? throw new UnauthorizedAccessException("Runner was not found.");
        if (RunnerPurposes.Normalize(runner.Purpose) != RunnerPurposes.Managed ||
            RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) != RunnerApplicationProfiles.MachineManagement ||
            runner.ApiKeyId != keyId)
            throw new UnauthorizedAccessException("Only enrolled Machine Management Runners may execute operations.");
    }
}
