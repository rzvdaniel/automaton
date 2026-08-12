namespace Automaton.AgentOrchestrator.Integration;

public static class OrchestratorIntegrationSchema
{
    public const int CurrentVersion = 1;
}

public static class OrchestratorAvailabilityStates
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
}

public sealed record OrchestratorStatusQueryResult(
    int SchemaVersion,
    string Availability,
    OrchestratorStatusContract? Status = null,
    string? Reason = null);

public sealed record OrchestratorStatusContract(
    DateTimeOffset GeneratedAtUtc,
    string DaemonState,
    bool StartupReconciled,
    bool Healthy,
    string RepositoryIdentity,
    string ApplicationVersion,
    string BuildRevision,
    IReadOnlyList<OrchestratorRunContract> Runs,
    string? LastError = null);

public sealed record OrchestratorRunContract(
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

public interface IOrchestratorStatusQuery
{
    Task<OrchestratorStatusQueryResult> GetAsync(CancellationToken cancellationToken);
    Task<OrchestratorRunContract?> GetRunAsync(string workItemId, CancellationToken cancellationToken);
}
