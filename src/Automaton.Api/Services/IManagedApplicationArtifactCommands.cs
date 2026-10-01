using Automaton.Core.Models;

namespace Automaton.Api.Services;

public interface IManagedApplicationArtifactCommands
{
    Task<SynchronizeApplicationManagedArtifactsResult> SynchronizeAsync(Guid runnerId, Guid ownerId,
        SynchronizeApplicationManagedArtifactsRequest request, Guid? deploymentOperationId,
        CancellationToken cancellationToken);
}
