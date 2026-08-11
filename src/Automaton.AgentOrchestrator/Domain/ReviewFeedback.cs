namespace Automaton.AgentOrchestrator.Domain;

public static class ReviewFeedbackStatuses
{
    public const string AwaitingHuman = "awaiting-human";
    public const string Actionable = "actionable";
    public const string Approved = "approved";
}

public static class ReviewFeedbackKinds
{
    public const string ReviewComment = "review-comment";
    public const string ChangesRequested = "changes-requested";
}

public sealed record ReviewFeedbackTarget(string RepositoryIdentity, int PullRequestNumber, string HeadBranch, string HeadSha, int RemediationRound = 0, int PublicationRevision = 1);
public sealed record ReviewFeedbackItem(string Id, string Kind, string Author, string Body, string? Path = null, int? Line = null, string? CommitSha = null, string? Url = null);
public sealed record ReviewFeedbackSnapshot(string HeadSha, string Status, IReadOnlyList<ReviewFeedbackItem> Items);
public sealed record ReviewFeedbackRecoveryState(string Status, string HeadSha, int RemediationRound, DateTimeOffset? LastObservedAtUtc = null);
public sealed record ReviewFeedbackRemediationRequest(ReviewFeedbackTarget Target, Workspace Workspace, WorkItem WorkItem, ReviewFeedbackSnapshot Feedback, int RemediationRound);
public sealed record ReviewFeedbackResult(bool Succeeded, string? FailureReason, string HeadSha, int RemediationRound)
{
    public static ReviewFeedbackResult Success(string sha, int round) => new(true, null, sha, round);
    public static ReviewFeedbackResult Blocked(string reason, string sha, int round) => new(false, reason, sha, round);
}
