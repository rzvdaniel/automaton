using Automaton.AgentOrchestrator.Domain;

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

public interface IClock { DateTimeOffset UtcNow { get; } }

public interface IWorkspaceMutationGuard
{
    Task<string> CaptureAsync(Workspace workspace, CancellationToken cancellationToken);
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
    Workspace? CurrentWorkspace { get; }
    CodexProcessIdentity? ProcessIdentity { get; }
    DateTimeOffset LastActivityUtc { get; }
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
    ICodexAppServerClient Create(Workspace workspace, IReadOnlyDictionary<string, string>? childEnvironment = null);
}
public interface IRunStateStore { }
