namespace Automaton.AgentOrchestrator.Domain;

public static class CiStatuses
{
    public const string Pending = "pending";
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Indeterminate = "indeterminate";
}

public sealed record CiTarget(string RepositoryIdentity, int PullRequestNumber, string HeadBranch, string HeadSha);
public sealed record CiCheck(string Name, string Status, string? Conclusion = null, string? Url = null, string? FailureSummary = null, string? ProviderId = null);
public sealed record CiSnapshot(string HeadSha, string Status, IReadOnlyList<CiCheck> Checks);
public sealed record CiRecoveryState(string Status, string HeadSha, int RemediationRound, DateTimeOffset? LastObservedAtUtc = null, string? FailureReason = null);
public sealed record CiRemediationRequest(CiTarget Target, Workspace Workspace, WorkItem WorkItem, CiSnapshot Failure, int RemediationRound);

public sealed record CiShepherdResult(bool Succeeded, bool IsRetryable, string? FailureReason = null, string? HeadSha = null, int RemediationRound = 0)
{
    public static CiShepherdResult Success(string sha, int round) => new(true, false, HeadSha: sha, RemediationRound: round);
    public static CiShepherdResult Blocked(string reason, string sha, int round) => new(false, false, reason, sha, round);
    public static CiShepherdResult Retryable(string reason, string sha, int round) => new(false, true, reason, sha, round);
}
