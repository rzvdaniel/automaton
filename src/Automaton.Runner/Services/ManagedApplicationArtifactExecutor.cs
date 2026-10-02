using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class ManagedApplicationArtifactExecutor(RunnerWorkspaceService workspace)
{
    public RunnerExecutionResult Execute(SynchronizeApplicationManagedArtifactsRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.AppId == Guid.Empty || request.DeploymentTargetId == Guid.Empty)
            throw new InvalidDataException("Managed artifacts require application and deployment-target identities.");
        var started = DateTime.UtcNow;
        ManagedApplicationArtifactSynchronizer.Replace(
            workspace.GetAppFlowArtifactsRoot(request.AppId, request.DeploymentTargetId),
            request.Files ?? [], workspace.NormalizeRelativePath);
        return new()
        {
            Outcome = "Succeeded", Started = started, Finished = DateTime.UtcNow,
            ManagedArtifactsResult = new(request.AppId, request.DeploymentTargetId, true)
        };
    }
}
