namespace Automaton.Runner.Console.Services;

public sealed record RunnerBackendRegistration(Guid AppId, int Port);

/// <summary>Runtime-owned mapping from deployed apps to loopback backend ports.</summary>
public sealed class RunnerBackendRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, RunnerBackendRegistration> registrations = [];

    public void Register(Guid appId, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        lock (gate) registrations[appId] = new RunnerBackendRegistration(appId, port);
    }

    public bool TryGet(Guid appId, out RunnerBackendRegistration registration)
    {
        lock (gate) return registrations.TryGetValue(appId, out registration!);
    }

    public void Remove(Guid appId)
    {
        lock (gate) registrations.Remove(appId);
    }
}
