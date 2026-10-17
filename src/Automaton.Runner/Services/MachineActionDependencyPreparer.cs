using Automaton.Core.Models;
using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Services;

public sealed class MachineActionDependencyPreparer(HostContainerExecutionService execution)
{
    public bool IsMachineManagement
        => RunnerApplicationProfiles.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            == RunnerApplicationProfiles.MachineManagement;

    public static void EnsurePolicy(ActionContainerPolicy? policy)
    {
        if (policy?.IsValid() != true)
            throw new InvalidOperationException("Action dependency preparation is paused because its bounded container policy is unavailable.");
    }

    public async Task PreparePythonAsync(
        Guid appId,
        Guid targetId,
        string actionKey,
        Guid stagingId,
        ActionContainerPolicy policy,
        CancellationToken cancellationToken)
    {
        var response = await execution.ExecuteAsync(new HostContainerExecutionRequest
        {
            OperationId = stagingId,
            Scope = MachineActionExecutionPolicy.CreateScope(appId, targetId),
            Operation = HostContainerOperationKinds.PrepareActionDependencies,
            RequestedImage = policy.RuntimeImage,
            ActionKey = actionKey,
            ActionExecutionId = stagingId,
            ActionStagingId = stagingId,
            ContainerLimits = policy.Limits,
            SharedMemoryMb = policy.SharedMemoryMb,
            DockerLogMaxSizeMb = policy.DockerLogMaxSizeMb,
            DockerLogMaxFiles = policy.DockerLogMaxFiles,
            Resources = Resources(),
            TimeoutSeconds = 600
        }, cancellationToken);
        if (!response.Success)
            throw new InvalidOperationException($"Action dependency preparation failed ({response.ErrorCode}): {response.Error}");
    }

    private static HostContainerResourceRef Resources()
        => new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
            HostContainerResourceKinds.ApplicationData);
}
