namespace Automaton.AgentOrchestrator.Domain;

public static class WorkspaceRecoveryStates
{
    public const string Ready = "ready";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string RetryableFailure = "retryable-failure";
    public const string Ineligible = "ineligible";
}

public static class ExecutionFencingKinds
{
    public const string WindowsJob = "windows-job";
    public const string UnixProcessGroup = "unix-process-group";
    public const string PortableProcessTree = "portable-process-tree";
}

public sealed record DurableExecutionIdentity(
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string LaunchNonce,
    string FencingKind,
    int? ProcessGroupId = null,
    DateTimeOffset? ExecutionStartedAtUtc = null);

public sealed record WorkspaceRecoveryState(
    string State,
    int LastCompletedAttempt = 0,
    int NextAttempt = 1,
    DateTimeOffset? RetryNotBeforeUtc = null,
    string? Reason = null);

public sealed record OwnedWorkspaceRecord(
    Workspace Workspace,
    string WorkItemId,
    WorkspaceRepository Repository,
    WorkspaceRecoveryState Recovery,
    DurableExecutionIdentity? ExecutionIdentity,
    int SchemaVersion,
    PublicationRecoveryState? Publication = null,
    CiRecoveryState? Ci = null);
