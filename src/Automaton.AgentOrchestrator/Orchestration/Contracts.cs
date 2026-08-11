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

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public interface IWorkspaceManager
{
    Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken);
    Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string lifecycleStatus, CancellationToken cancellationToken);
    Task CleanupTerminalAsync(Workspace workspace, CancellationToken cancellationToken);
}
public interface ICodexAppServerClient { }
public interface IRunStateStore { }
