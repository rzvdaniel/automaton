using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed class HostContainerBuildCleanupService(InfrastructureCommandService docker)
{
    public async Task CleanupAfterResultAsync(HostContainerExecutionRequest request)
    {
        if (request.Operation != HostContainerOperationKinds.RunBuild || request.Scope is null
            || request.Scope.DeploymentAttemptId == Guid.Empty)
            return;

        try
        {
            await docker.ExecuteTypedDockerAsync(
                ["docker", "rm", "-f", HostContainerBuildContainerName.For(request.Scope)],
                TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch
        {
            // Cleanup is best effort and must not replace the persisted build outcome.
        }
    }
}

internal static class HostContainerBuildContainerName
{
    public static string For(HostContainerScope scope)
        => $"automaton-build-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{scope.DeploymentAttemptId:N}";
}
