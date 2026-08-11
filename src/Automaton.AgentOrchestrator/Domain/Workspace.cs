namespace Automaton.AgentOrchestrator.Domain;

public static class WorkspaceLifecycleStates
{
    public const string Preparing = "preparing";
    public const string Ready = "ready";
    public const string RetryableFailure = "retryable-failure";
    public const string Failed = "failed";
    public const string Terminal = "terminal";
    public const string Active = "active";
    public const string Quarantined = "quarantined";
}

public sealed record WorkspaceRepository(string RootPath, string Identity);
public sealed record WorkspaceRequest(WorkItem WorkItem, WorkspaceRepository Repository);

public sealed record Workspace(
    string Key,
    string Path,
    string Branch,
    string LifecycleStatus,
    string LaunchNonce,
    string RepositoryIdentity);
