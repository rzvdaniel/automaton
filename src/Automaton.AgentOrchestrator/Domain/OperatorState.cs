namespace Automaton.AgentOrchestrator.Domain;

public static class OperatorDaemonStates
{
    public const string Starting = "starting";
    public const string Reconciling = "reconciling";
    public const string Running = "running";
    public const string Stopping = "stopping";
    public const string Degraded = "degraded";
}

public static class OperatorEventSeverities
{
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record OperatorEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string EventType,
    string Severity,
    string? WorkItemId = null,
    string? Stage = null,
    string? Role = null,
    string? Message = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record OperatorRunStatus(
    string WorkItemId,
    string RunState,
    int Attempt,
    DateTimeOffset UpdatedAtUtc,
    string? Stage = null,
    string? Role = null,
    string? WorkspaceKey = null,
    string? WorkspacePath = null,
    string? Branch = null,
    string? PullRequestUrl = null,
    string? HeadSha = null,
    string? CiStatus = null,
    string? FeedbackStatus = null,
    string? OwnershipStatus = null,
    DateTimeOffset? RetryAtUtc = null,
    string? Reason = null,
    DateTimeOffset? CiObservedAtUtc = null,
    DateTimeOffset? FeedbackObservedAtUtc = null);

public sealed record OperatorStatusSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string DaemonState,
    bool StartupReconciled,
    bool Healthy,
    string RepositoryIdentity,
    IReadOnlyList<OperatorRunStatus> Runs,
    string? LastError = null);
