using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Orchestration;

public interface IWorkTracker
{
    Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken);
    Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken);
}

public interface IWorkExecutor
{
    Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public interface IOperatorTelemetry
{
    void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null);
    OperatorEvent? LatestFor(string workItemId);
    void SetDaemonState(string state, string? error = null);
    (string State, string? LastError) DaemonState();
}

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public interface IWorkspaceManager
{
    Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken);
    Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string lifecycleStatus, CancellationToken cancellationToken);
    Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken);
    Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken cancellationToken);
    Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken);
    Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken cancellationToken);
    Task CleanupTerminalAsync(Workspace workspace, CancellationToken cancellationToken);
}
public interface ICodexAppServerClient : IAsyncDisposable
{
    CodexProcessIdentity? ProcessIdentity { get; }
    DateTimeOffset LastActivityUtc { get; }
    string? LastEventType => null;
    TimeSpan IdleTimeout => Timeout.InfiniteTimeSpan;
    bool? IsProcessAlive => null;
    IReadOnlyList<CodexSessionEvent> Events { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<CodexThread> StartThreadAsync(CancellationToken cancellationToken);
    Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken);
    Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken);
    Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken);
    Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken);
}

public interface ICodexAppServerClientFactory
{
    ICodexAppServerClient Create(WorkspaceExecutionContext context, IReadOnlyDictionary<string, string>? childEnvironment = null);
    ICodexAppServerClient Create(WorkspaceExecutionContext context, AgentExecutionProfile profile, IReadOnlyDictionary<string, string>? childEnvironment = null)
        => Create(context, childEnvironment);
}
public interface IRunStateStore { }
