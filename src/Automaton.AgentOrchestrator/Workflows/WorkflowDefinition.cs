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
    public PublicationOptions Publication { get; init; } = new();
    public CiOptions Ci { get; init; } = new();
    public ReviewFeedbackOptions ReviewFeedback { get; init; } = new();
    public WorkspaceOptions Workspace { get; init; } = new();
    public AgentExecutionOptions Agent { get; init; } = new();
    public CodexOptions Codex { get; init; } = new();
    public RetryOptions Retry { get; init; } = new();
    public ReconciliationOptions Reconciliation { get; init; } = new();
    public ObservabilityOptions Observability { get; init; } = new();
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
public sealed class RepositoryOptions { public string Root { get; init; } = string.Empty; public string Identity { get; init; } = string.Empty; public string BaseBranch { get; init; } = "main"; }
public sealed class PublicationOptions
{
    public IReadOnlyList<string> ValidationCommands { get; init; } = [];
    public string GitName { get; init; } = "Automaton Agent";
    public string GitEmail { get; init; } = "automaton-agent@users.noreply.github.com";
    public string RemoteName { get; init; } = "origin";
    public string GitHubHost { get; init; } = "github.com";
    public string GitHubToken { get; init; } = string.Empty;
    public int ValidationTimeoutMilliseconds { get; init; } = 600_000;
}
public sealed class CiOptions
{
    public bool Enabled { get; init; } = true;
    public string Kind { get; init; } = "github";
    public string GitHubToken { get; init; } = string.Empty;
    public string GitHubApiBaseUrl { get; init; } = "https://api.github.com/";
    public IReadOnlyList<string> RequiredChecks { get; init; } = [];
    public int PollIntervalMilliseconds { get; init; } = 30_000;
    public int TimeoutMilliseconds { get; init; } = 3_600_000;
    public int MaxRemediationRounds { get; init; } = 2;
}
public sealed class ReviewFeedbackOptions
{
    public bool Enabled { get; init; } = true;
    public string Kind { get; init; } = "github";
    public string GitHubToken { get; init; } = string.Empty;
    public string GitHubApiBaseUrl { get; init; } = "https://api.github.com/";
    public string AutomationLogin { get; init; } = string.Empty;
    public int PollIntervalMilliseconds { get; init; } = 60_000;
    public int MaxRemediationRounds { get; init; } = 3;
}
public sealed class WorkspaceOptions { public string Root { get; init; } = string.Empty; }
public sealed class AgentExecutionOptions
{
    public int MaxConcurrentAgents { get; init; } = 1;
    public int MaxTurns { get; init; } = 1;
    public int ManagerMaxTurns { get; init; } = 2;
    public int DeveloperMaxTurns { get; init; } = 1;
    public int ReviewerMaxTurns { get; init; } = 2;
    public int MaxReviewRounds { get; init; } = 3;
    public AgentGitHubOptions GitHub { get; init; } = new();
}
public sealed class AgentGitHubOptions
{
    public bool Enabled { get; init; }
    public string Token { get; init; } = string.Empty;
}
public sealed class CodexOptions { public string Command { get; init; } = "codex app-server"; public int TurnTimeoutMilliseconds { get; init; } = 3_600_000; }
public sealed class RetryOptions { public int MaxAttempts { get; init; } = 3; public int InitialBackoffMilliseconds { get; init; } = 1_000; public int MaxBackoffMilliseconds { get; init; } = 300_000; }
public sealed class ReconciliationOptions { public int IntervalMilliseconds { get; init; } = 30_000; }
public sealed class ObservabilityOptions
{
    public bool Enabled { get; init; } = true;
    public int SnapshotIntervalMilliseconds { get; init; } = 10_000;
    public int MaximumEventFileBytes { get; init; } = 5 * 1024 * 1024;
}
