namespace Automaton.AgentOrchestrator.Domain;

/// <summary>
/// Daemon-owned transient execution states. These values are intentionally distinct
/// from a tracker provider's lifecycle state and from provider labels.
/// </summary>
public static class RunStates
{
    public const string Eligible = "eligible";
    public const string Claimed = "claimed";
    public const string Working = "working";
    public const string RetryQueued = "retry-queued";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
}

public sealed record RunState(
    string WorkItemId,
    string State,
    int Attempt,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RetryAtUtc = null,
    string? FailureReason = null);
