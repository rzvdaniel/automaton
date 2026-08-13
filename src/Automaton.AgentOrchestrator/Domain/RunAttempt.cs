namespace Automaton.AgentOrchestrator.Domain;

public sealed record RunAttempt(WorkItem WorkItem, int Attempt, DateTimeOffset StartedAtUtc);

public sealed record RunOutcome(bool Succeeded, bool IsRetryable, string? FailureReason = null, string? FailureCategory = null, string? FailureSubcategory = null)
{
    public static RunOutcome Success() => new(true, false);
    public static RunOutcome RetryableFailure(string reason) => new(false, true, reason);
    public static RunOutcome PermanentFailure(string reason, string? category = null, string? subcategory = null) => new(false, false, reason, category, subcategory);
}
