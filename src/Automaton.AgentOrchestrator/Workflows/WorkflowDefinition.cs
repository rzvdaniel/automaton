namespace Automaton.AgentOrchestrator.Workflows;

public sealed record WorkflowDefinition(
    string SourcePath,
    IReadOnlyDictionary<string, object?> FrontMatter,
    string PromptTemplate,
    OrchestratorOptions Options);

public sealed class OrchestratorOptions
{
    public TrackerOptions Tracker { get; init; } = new();
    public PollingOptions Polling { get; init; } = new();
    public RepositoryOptions Repository { get; init; } = new();
    public WorkspaceOptions Workspace { get; init; } = new();
    public AgentExecutionOptions Agent { get; init; } = new();
    public CodexOptions Codex { get; init; } = new();
    public RetryOptions Retry { get; init; } = new();
    public ReconciliationOptions Reconciliation { get; init; } = new();
}

public sealed class TrackerOptions
{
    public string Kind { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Provider { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> RequiredLabels { get; init; } = [];
    public IReadOnlyList<string> ActiveProviderLifecycleStates { get; init; } = ["open"];
    public IReadOnlyList<string> TerminalProviderLifecycleStates { get; init; } = ["closed"];
}

public sealed class PollingOptions { public int IntervalMilliseconds { get; init; } = 30_000; }
public sealed class RepositoryOptions { public string Root { get; init; } = string.Empty; public string Identity { get; init; } = string.Empty; }
public sealed class WorkspaceOptions { public string Root { get; init; } = string.Empty; }
public sealed class AgentExecutionOptions
{
    public int MaxConcurrentAgents { get; init; } = 1;
    public int MaxTurns { get; init; } = 1;
    public int ManagerMaxTurns { get; init; } = 2;
    public int DeveloperMaxTurns { get; init; } = 1;
    public int ReviewerMaxTurns { get; init; } = 2;
    public int MaxReviewRounds { get; init; } = 3;
}
public sealed class CodexOptions { public string Command { get; init; } = "codex app-server"; public int TurnTimeoutMilliseconds { get; init; } = 3_600_000; }
public sealed class RetryOptions { public int MaxAttempts { get; init; } = 3; public int InitialBackoffMilliseconds { get; init; } = 1_000; public int MaxBackoffMilliseconds { get; init; } = 300_000; }
public sealed class ReconciliationOptions { public int IntervalMilliseconds { get; init; } = 30_000; }
