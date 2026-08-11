using Automaton.AgentOrchestrator.Domain;

namespace Automaton.AgentOrchestrator.Orchestration;

public interface IWorkTracker
{
    Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken);
    Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken);
}

public interface ICiProvider
{
    Task<CiSnapshot> GetAsync(CiTarget target, CancellationToken cancellationToken);
}

public interface ICiShepherd
{
    Task<CiShepherdResult> ShepherdAsync(CiTarget target, Workspace workspace, WorkItem workItem, CancellationToken cancellationToken);
}

public interface ICiRemediator
{
    Task<PublicationResult> RemediateAsync(CiRemediationRequest request, CancellationToken cancellationToken);
}

public interface ICiPollDelay { Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken); }

public interface IReviewFeedbackProvider
{
    Task<ReviewFeedbackSnapshot> GetAsync(ReviewFeedbackTarget target, CancellationToken cancellationToken);
}

public interface IReviewFeedbackShepherd
{
    Task<ReviewFeedbackResult> ShepherdAsync(ReviewFeedbackTarget target, Workspace workspace, WorkItem workItem, CancellationToken cancellationToken);
}

public interface IReviewFeedbackRemediator
{
    Task<PublicationResult> RemediateAsync(ReviewFeedbackRemediationRequest request, CancellationToken cancellationToken);
}

public interface IReviewFeedbackPollDelay { Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken); }

public interface IWorkExecutor
{
    Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken);
}

public interface IWorkPublisher
{
    Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken cancellationToken);
    Task<PublicationResult> PublishRevisionAsync(PublicationRevisionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task ValidateRevisionBaseAsync(Workspace workspace, PublicationResult currentPublication, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<PublicationRecoveryResult> RecoverAsync(OwnedWorkspaceRecord record, WorkItem workItem, CancellationToken cancellationToken);
}

public interface IPullRequestPublisher
{
    Task<PullRequestPublication> FindOrCreateAsync(PullRequestPublicationRequest request, CancellationToken cancellationToken);
    Task<PullRequestPublication?> FindExactAsync(PullRequestPublicationRequest request, int? expectedNumber, CancellationToken cancellationToken);
}

public sealed record PullRequestPublicationRequest(
    string Repository,
    string HeadBranch,
    string HeadSha,
    string BaseBranch,
    string Title,
    string Body);

public interface IClock { DateTimeOffset UtcNow { get; } }

public interface IOperatorTelemetry
{
    void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null);
    OperatorEvent? LatestFor(string workItemId);
    void SetDaemonState(string state, string? error = null);
    (string State, string? LastError) DaemonState();
}

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
    Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState publicationState, CancellationToken cancellationToken);
    Task<Workspace> UpdateCiStateAsync(Workspace workspace, CiRecoveryState ciState, CancellationToken cancellationToken) => Task.FromResult(workspace);
    Task<Workspace> UpdateReviewFeedbackStateAsync(Workspace workspace, ReviewFeedbackRecoveryState feedbackState, CancellationToken cancellationToken) => Task.FromResult(workspace);
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
