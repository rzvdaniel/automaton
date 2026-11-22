namespace Automaton.Runner.Console.Services;

public readonly record struct DeploymentRuntimeKey(Guid AppId, Guid DeploymentTargetId);

public sealed record RunnerRuntimeRegistration(
    Guid AppId,
    Guid DeploymentTargetId,
    int Port);

/// <summary>A runtime the Runner could not start (Failed), or one that came back after failing (not Failed, sent once).</summary>
public sealed record RunnerRuntimeStateReport(Guid AppId, Guid DeploymentTargetId, bool Failed, string? Error);

/// <summary>Runtime-owned mapping from deployed applications to loopback ports.</summary>
public sealed class RunnerRuntimeRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, RunnerRuntimeRegistration> registrations = [];
    private readonly Dictionary<DeploymentRuntimeKey, RunnerRuntimeStateReport> reports = [];

    public void Register(Guid appId, Guid deploymentTargetId, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        lock (gate)
        {
            registrations[key] = new RunnerRuntimeRegistration(appId, deploymentTargetId, port);
            if (reports.TryGetValue(key, out var report) && report.Failed)
                reports[key] = report with { Failed = false, Error = null };
        }
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
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        lock (gate)
        {
            registrations.Remove(key);
            reports.Remove(key);
        }
    }

    public IReadOnlyList<RunnerRuntimeRegistration> Snapshot()
    {
        lock (gate)
            return registrations.Values.ToArray();
    }

    public bool IsPortRegistered(int port)
    {
        lock (gate)
            return registrations.Values.Any(registration => registration.Port == port);
    }

    /// <summary>Records that the runtime could not be started, so the server shows it down instead of live.</summary>
    public void MarkFailed(Guid appId, Guid deploymentTargetId, string error)
    {
        lock (gate)
            reports[new DeploymentRuntimeKey(appId, deploymentTargetId)] = new(appId, deploymentTargetId, true, error);
    }

    /// <summary>A probed runtime that is running normally again: a failure recorded by the probe is cleared and sent once.</summary>
    public void MarkRecovered(Guid appId, Guid deploymentTargetId)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        lock (gate)
            if (reports.TryGetValue(key, out var report) && report.Failed)
                reports[key] = report with { Failed = false, Error = null };
    }

    public IReadOnlyList<RunnerRuntimeStateReport> PendingReports()
    {
        lock (gate)
            return reports.Values.ToArray();
    }

    /// <summary>
    /// Pending reports plus a "running" report for every other registered runtime, so a target the server marked
    /// failed (a failed deploy while the previous version kept running) follows the runtime. Memory only, no inspection.
    /// </summary>
    public IReadOnlyList<RunnerRuntimeStateReport> StateReports()
    {
        lock (gate)
            return reports.Values.Concat(registrations.Keys.Where(key => !reports.ContainsKey(key))
                .Select(key => new RunnerRuntimeStateReport(key.AppId, key.DeploymentTargetId, false, null))).ToArray();
    }

    /// <summary>Drops the recovered entries the server has received; a failed one is sent again until the runtime is back.</summary>
    public void Acknowledge(IEnumerable<RunnerRuntimeStateReport> sent)
    {
        lock (gate)
            foreach (var report in sent.Where(item => !item.Failed))
            {
                var key = new DeploymentRuntimeKey(report.AppId, report.DeploymentTargetId);
                if (reports.TryGetValue(key, out var current) && !current.Failed) reports.Remove(key);
            }
    }
}
