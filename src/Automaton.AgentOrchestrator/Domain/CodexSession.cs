namespace Automaton.AgentOrchestrator.Domain;

public static class CodexSessionEventKinds
{
    public const string ProcessStarted = "process-started";
    public const string Initialized = "initialized";
    public const string ThreadStarted = "thread-started";
    public const string ThreadResumed = "thread-resumed";
    public const string TurnStarted = "turn-started";
    public const string ItemActivity = "item-activity";
    public const string TurnCompleted = "turn-completed";
    public const string TurnFailed = "turn-failed";
    public const string TurnInterrupted = "turn-interrupted";
    public const string ProcessExited = "process-exited";
    public const string ProtocolFailure = "protocol-failure";
    public const string UsageUpdated = "usage-updated";
}

public sealed record CodexSessionEvent(
    string Kind,
    DateTimeOffset AtUtc,
    string? ThreadId = null,
    string? TurnId = null,
    string? Detail = null,
    long? InputTokens = null,
    long? OutputTokens = null);

public sealed record CodexThread(string Id);
public sealed record CodexTurn(string Id, string ThreadId);
public sealed record CodexTurnResult(string ThreadId, string TurnId, string Status, string? FailureReason = null, string? FinalResponse = null);
public sealed record CodexProcessIdentity(int ProcessId, DateTimeOffset StartedAtUtc, string LaunchNonce);
