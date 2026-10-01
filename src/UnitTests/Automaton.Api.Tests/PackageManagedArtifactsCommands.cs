using Automaton.Api.Services;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

internal sealed class PackageManagedArtifactsCommands : IManagedApplicationArtifactCommands
{
    public Task<SynchronizeApplicationManagedArtifactsResult> SynchronizeAsync(Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, Guid? deploymentOperationId, CancellationToken cancellationToken)
        => Task.FromResult(new SynchronizeApplicationManagedArtifactsResult(request.AppId, request.DeploymentTargetId, true));
}
