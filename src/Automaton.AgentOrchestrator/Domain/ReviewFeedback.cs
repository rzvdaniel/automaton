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
public static class ReviewFeedbackFailureCategories
{
    public const string Provider = "human-feedback-provider-failed";
    public const string Developer = "human-feedback-developer-failed";
    public const string Reviewer = "human-feedback-reviewer-failed";
    public const string Publication = "human-feedback-publication-failed";
    public const string Ci = "human-feedback-ci-failed";
    public const string MonitorUnexpected = "human-feedback-monitor-unexpected";
}

public sealed record ReviewFeedbackTarget(string RepositoryIdentity, int PullRequestNumber, string HeadBranch, string HeadSha, int RemediationRound = 0, int PublicationRevision = 1);
public sealed record ReviewFeedbackItem(string Id, string Kind, string Author, string Body, string? Path = null, int? Line = null, string? CommitSha = null, string? Url = null);
public sealed record ReviewFeedbackSnapshot(string HeadSha, string Status, IReadOnlyList<ReviewFeedbackItem> Items);
public sealed record ReviewFeedbackRecoveryState(string Status, string HeadSha, int RemediationRound, DateTimeOffset? LastObservedAtUtc = null, IReadOnlyList<ConsumedReviewFeedback>? Consumed = null);
public sealed record ConsumedReviewFeedback(string RepositoryIdentity, int PullRequestNumber, string FeedbackId, string Version, string SatisfiedHeadSha, int SatisfiedRevision, DateTimeOffset SatisfiedAtUtc);
public sealed record ReviewFeedbackRemediationRequest(ReviewFeedbackTarget Target, Workspace Workspace, WorkItem WorkItem, ReviewFeedbackSnapshot Feedback, int RemediationRound);
public sealed record ReviewFeedbackResult(bool Succeeded, string? FailureReason, string HeadSha, int RemediationRound, string? FailureCategory = null, bool IsRetryable = false, string? FailureSubcategory = null)
{
    public static ReviewFeedbackResult Success(string sha, int round) => new(true, null, sha, round);
    public static ReviewFeedbackResult Blocked(string reason, string sha, int round, string? category = null, bool isRetryable = false) => new(false, reason, sha, round, category, isRetryable);
}
