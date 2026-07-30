namespace Automaton.Runner.Console.Services;

public readonly record struct DeploymentRuntimeKey(Guid AppId, Guid DeploymentTargetId);

public sealed record RunnerBackendRegistration(
    Guid AppId,
    Guid DeploymentTargetId,
    int Port);

/// <summary>Runtime-owned mapping from deployed apps to loopback backend ports.</summary>
public sealed class RunnerBackendRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, RunnerBackendRegistration> registrations = [];

    public void Register(Guid appId, Guid deploymentTargetId, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        lock (gate)
            registrations[key] = new RunnerBackendRegistration(appId, deploymentTargetId, port);
    }

    public bool TryGet(
        Guid appId,
        Guid deploymentTargetId,
        out RunnerBackendRegistration registration)
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
