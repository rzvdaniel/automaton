namespace Automaton.Runner.Console.Services;

public readonly record struct DeploymentRuntimeKey(Guid AppId, Guid DeploymentTargetId);

public sealed record RunnerRuntimeRegistration(
    Guid AppId,
    Guid DeploymentTargetId,
    int Port);

/// <summary>Runtime-owned mapping from deployed applications to loopback ports.</summary>
public sealed class RunnerRuntimeRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, RunnerRuntimeRegistration> registrations = [];

    public void Register(Guid appId, Guid deploymentTargetId, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        lock (gate)
            registrations[key] = new RunnerRuntimeRegistration(appId, deploymentTargetId, port);
    }

    public bool TryGet(
        Guid appId,
        Guid deploymentTargetId,
        out RunnerRuntimeRegistration registration)
    {
        lock (gate)
            return registrations.TryGetValue(
                new DeploymentRuntimeKey(appId, deploymentTargetId),
                out registration!);
    }

    public void Remove(Guid appId, Guid deploymentTargetId)
    {
        lock (gate)
            registrations.Remove(new DeploymentRuntimeKey(appId, deploymentTargetId));
    }
}
