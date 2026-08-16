using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Automaton.AgentOrchestrator.Workflows;

public sealed class WorkflowLoader : IWorkflowLoader
{
    private readonly IDeserializer deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public async Task<WorkflowDefinition> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new WorkflowValidationException("WORKFLOW.md was not found.");

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var (frontMatter, promptTemplate) = ParseDocument(text);
        var options = BuildOptions(frontMatter, Path.GetDirectoryName(fullPath)!);
        Validate(options);
        return new WorkflowDefinition(fullPath, frontMatter, promptTemplate, options);
    }

    internal (IReadOnlyDictionary<string, object?> FrontMatter, string PromptTemplate) ParseDocument(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
            return (new Dictionary<string, object?>(), text.Trim());

        var closingMarker = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closingMarker < 0) throw new WorkflowValidationException("WORKFLOW.md front matter is not terminated.");
        var yaml = text[3..closingMarker].Trim('\r', '\n');
        var bodyStart = closingMarker + 4;
        if (bodyStart < text.Length && text[bodyStart] == '\r') bodyStart++;
        if (bodyStart < text.Length && text[bodyStart] == '\n') bodyStart++;

        try
        {
            var parsed = deserializer.Deserialize<object?>(yaml);
            if (parsed is not IDictionary<object, object> map)
                throw new WorkflowValidationException("WORKFLOW.md front matter must be a mapping.");
            return (ConvertMap(map), text[bodyStart..].Trim());
        }
        catch (YamlException)
        {
            throw new WorkflowValidationException("WORKFLOW.md front matter is invalid YAML.");
        }
    }

    private static IReadOnlyDictionary<string, object?> ConvertMap(IDictionary<object, object> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (pair.Key is not string key) throw new WorkflowValidationException("WORKFLOW.md front matter keys must be strings.");
            result[key] = ConvertValue(pair.Value);
        }
        return result;
    }

    private static object? ConvertValue(object? value) => value switch
    {
        IDictionary<object, object> map => ConvertMap(map),
        IEnumerable<object> values when value is not string => values.Select(ConvertValue).ToArray(),
        _ => value
    };

    private static OrchestratorOptions BuildOptions(IReadOnlyDictionary<string, object?> root, string workflowDirectory)
    {
        var tracker = Section(root, "tracker");
        var provider = Section(tracker, "provider")
            .ToDictionary(x => x.Key, x => ResolveProviderValue(x.Key, StringValue(x.Value, $"tracker.provider.{x.Key}")!), StringComparer.OrdinalIgnoreCase);
        var repository = Section(root, "repository");
        var repositoryRoot = ResolvePath(StringValue(repository.GetValueOrDefault("root"), "repository.root", required: false) ?? ".", workflowDirectory);
        var repositoryIdentity = StringValue(repository.GetValueOrDefault("identity"), "repository.identity", required: false)
            ?? new DirectoryInfo(repositoryRoot).Name;
        var workspaceRoot = ResolvePath(StringValue(Section(root, "workspace").GetValueOrDefault("root"), "workspace.root", required: false) ?? string.Empty, workflowDirectory);
        return new OrchestratorOptions
        {
            Tracker = new TrackerOptions
            {
                Kind = StringValue(tracker.GetValueOrDefault("kind"), "tracker.kind") ?? string.Empty,
                Provider = provider,
                RequiredLabels = StringList(tracker.GetValueOrDefault("required_labels"), "tracker.required_labels"),
                ActiveProviderLifecycleStates = StringList(tracker.GetValueOrDefault("active_states"), "tracker.active_states", ["open"]),
                TerminalProviderLifecycleStates = StringList(tracker.GetValueOrDefault("terminal_states"), "tracker.terminal_states", ["closed"])
            },
            Polling = new PollingOptions { IntervalMilliseconds = PositiveInt(Section(root, "polling").GetValueOrDefault("interval_ms"), "polling.interval_ms", 30_000) },
            Repository = new RepositoryOptions
            {
                Root = repositoryRoot,
                Identity = repositoryIdentity,
                BaseBranch = StringValue(repository.GetValueOrDefault("base_branch"), "repository.base_branch", required: false) ?? "main"
            },
            Publication = new PublicationOptions
            {
                ValidationCommands = StringList(Section(root, "publication").GetValueOrDefault("validation_commands"), "publication.validation_commands"),
                GitName = StringValue(Section(root, "publication").GetValueOrDefault("git_name"), "publication.git_name", required: false) ?? "Automaton Agent",
                GitEmail = StringValue(Section(root, "publication").GetValueOrDefault("git_email"), "publication.git_email", required: false) ?? "automaton-agent@users.noreply.github.com",
                RemoteName = StringValue(Section(root, "publication").GetValueOrDefault("remote_name"), "publication.remote_name", required: false) ?? "origin",
                GitHubHost = StringValue(Section(root, "publication").GetValueOrDefault("github_host"), "publication.github_host", required: false) ?? "github.com",
                GitHubToken = ResolveOptionalSecret(Section(root, "publication").GetValueOrDefault("github_token"), "publication.github_token"),
                ValidationTimeoutMilliseconds = PositiveInt(Section(root, "publication").GetValueOrDefault("validation_timeout_ms"), "publication.validation_timeout_ms", 600_000)
            },
            Ci = new CiOptions
            {
                Enabled = BooleanValue(Section(root, "ci").GetValueOrDefault("enabled"), "ci.enabled", true),
                Kind = StringValue(Section(root, "ci").GetValueOrDefault("kind"), "ci.kind", required: false) ?? "github",
                GitHubToken = ResolveOptionalSecret(Section(root, "ci").GetValueOrDefault("github_token"), "ci.github_token"),
                GitHubApiBaseUrl = StringValue(Section(root, "ci").GetValueOrDefault("github_api_base_url"), "ci.github_api_base_url", required: false) ?? "https://api.github.com/",
                RequiredChecks = StringList(Section(root, "ci").GetValueOrDefault("required_checks"), "ci.required_checks"),
                PollIntervalMilliseconds = PositiveInt(Section(root, "ci").GetValueOrDefault("poll_interval_ms"), "ci.poll_interval_ms", 30_000),
                TimeoutMilliseconds = PositiveInt(Section(root, "ci").GetValueOrDefault("timeout_ms"), "ci.timeout_ms", 3_600_000),
                MaxRemediationRounds = NonNegativeInt(Section(root, "ci").GetValueOrDefault("max_remediation_rounds"), "ci.max_remediation_rounds", 2, 10)
            },
            ReviewFeedback = new ReviewFeedbackOptions
            {
                Enabled = BooleanValue(Section(root, "review_feedback").GetValueOrDefault("enabled"), "review_feedback.enabled", true),
                Kind = StringValue(Section(root, "review_feedback").GetValueOrDefault("kind"), "review_feedback.kind", required: false) ?? "github",
                GitHubToken = ResolveOptionalSecret(Section(root, "review_feedback").GetValueOrDefault("github_token"), "review_feedback.github_token"),
                GitHubApiBaseUrl = StringValue(Section(root, "review_feedback").GetValueOrDefault("github_api_base_url"), "review_feedback.github_api_base_url", required: false) ?? "https://api.github.com/",
                AutomationLogin = StringValue(Section(root, "review_feedback").GetValueOrDefault("automation_login"), "review_feedback.automation_login", required: false) ?? string.Empty,
                PollIntervalMilliseconds = PositiveInt(Section(root, "review_feedback").GetValueOrDefault("poll_interval_ms"), "review_feedback.poll_interval_ms", 60_000),
                MaxRemediationRounds = NonNegativeInt(Section(root, "review_feedback").GetValueOrDefault("max_remediation_rounds"), "review_feedback.max_remediation_rounds", 3, 10)
            },
            Workspace = new WorkspaceOptions { Root = workspaceRoot },
            Agent = new AgentExecutionOptions
            {
                MaxConcurrentAgents = PositiveInt(Section(root, "agent").GetValueOrDefault("max_concurrent_agents"), "agent.max_concurrent_agents", 1),
                MaxTurns = PositiveInt(Section(root, "agent").GetValueOrDefault("max_turns"), "agent.max_turns", 1),
                ManagerMaxTurns = PositiveInt(Section(root, "agent").GetValueOrDefault("manager_max_turns"), "agent.manager_max_turns", 2),
                DeveloperMaxTurns = PositiveInt(Section(root, "agent").GetValueOrDefault("developer_max_turns"), "agent.developer_max_turns",
                    PositiveInt(Section(root, "agent").GetValueOrDefault("max_turns"), "agent.max_turns", 1)),
                ReviewerMaxTurns = PositiveInt(Section(root, "agent").GetValueOrDefault("reviewer_max_turns"), "agent.reviewer_max_turns", 2),
                MaxReviewRounds = BoundedPositiveInt(Section(root, "agent").GetValueOrDefault("max_review_rounds"), "agent.max_review_rounds", 3, 10),
                GitHub = new AgentGitHubOptions
                {
                    Enabled = BooleanValue(Section(Section(root, "agent"), "github").GetValueOrDefault("enabled"), "agent.github.enabled", false),
                    Token = ResolveOptionalSecret(Section(Section(root, "agent"), "github").GetValueOrDefault("token"), "agent.github.token")
                }
            },
            Codex = new CodexOptions
            {
                Command = StringValue(Section(root, "codex").GetValueOrDefault("command"), "codex.command", required: false) ?? "codex app-server",
                TurnTimeoutMilliseconds = PositiveInt(Section(root, "codex").GetValueOrDefault("turn_timeout_ms"), "codex.turn_timeout_ms", 3_600_000)
            },
            Retry = new RetryOptions
            {
                MaxAttempts = PositiveInt(Section(root, "retry").GetValueOrDefault("max_attempts"), "retry.max_attempts", 3),
                InitialBackoffMilliseconds = PositiveInt(Section(root, "retry").GetValueOrDefault("initial_backoff_ms"), "retry.initial_backoff_ms", 1_000),
                MaxBackoffMilliseconds = PositiveInt(Section(root, "retry").GetValueOrDefault("max_backoff_ms"), "retry.max_backoff_ms", 300_000)
            },
            Reconciliation = new ReconciliationOptions { IntervalMilliseconds = PositiveInt(Section(root, "reconciliation").GetValueOrDefault("interval_ms"), "reconciliation.interval_ms", 30_000) },
            Observability = new ObservabilityOptions
            {
                Enabled = BooleanValue(Section(root, "observability").GetValueOrDefault("enabled"), "observability.enabled", true),
                SnapshotIntervalMilliseconds = PositiveInt(Section(root, "observability").GetValueOrDefault("snapshot_interval_ms"), "observability.snapshot_interval_ms", 10_000),
                MaximumEventFileBytes = PositiveInt(Section(root, "observability").GetValueOrDefault("maximum_event_file_bytes"), "observability.maximum_event_file_bytes", 5 * 1024 * 1024)
            }
        };
    }

    private static IReadOnlyDictionary<string, object?> Section(IReadOnlyDictionary<string, object?> root, string key)
        => root.GetValueOrDefault(key) switch
        {
            null => new Dictionary<string, object?>(),
            IReadOnlyDictionary<string, object?> section => section,
            _ => throw new WorkflowValidationException($"{key} must be a mapping.")
        };

    private static string? StringValue(object? value, string field, bool required = true)
    {
        if (value is null && !required) return null;
        if (value is not string result || string.IsNullOrWhiteSpace(result)) throw new WorkflowValidationException($"{field} must be a non-empty string.");
        return result;
    }

    private static IReadOnlyList<string> StringList(object? value, string field, IReadOnlyList<string>? defaultValue = null)
    {
        if (value is null) return defaultValue ?? [];
        if (value is not object?[] values || values.Any(x => x is not string or "")) throw new WorkflowValidationException($"{field} must be a list of non-empty strings.");
        return values.Cast<string>().ToArray();
    }

    private static int PositiveInt(object? value, string field, int defaultValue)
    {
        if (value is null) return defaultValue;
        if (!int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var number) || number <= 0)
            throw new WorkflowValidationException($"{field} must be a positive integer.");
        return number;
    }

    private static int BoundedPositiveInt(object? value, string field, int defaultValue, int maximum)
    {
        var result = PositiveInt(value, field, defaultValue);
        if (result > maximum) throw new WorkflowValidationException($"{field} must not exceed {maximum}.");
        return result;
    }

    private static int NonNegativeInt(object? value, string field, int defaultValue, int maximum)
    {
        if (value is null) return defaultValue;
        if (!int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var number) || number < 0 || number > maximum)
            throw new WorkflowValidationException($"{field} must be between 0 and {maximum}.");
        return number;
    }

    private static bool BooleanValue(object? value, string field, bool defaultValue)
    {
        if (value is null) return defaultValue;
        if (!bool.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var result))
            throw new WorkflowValidationException($"{field} must be true or false.");
        return result;
    }

    private static string ResolvePath(string value, string workflowDirectory)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(ResolveEnvironmentReference(value, "workspace.root"), workflowDirectory);

    private static string ResolveEnvironmentReference(string value, string field)
    {
        if (!value.StartsWith('$')) return value;
        var variable = value[1..];
        if (variable.Length == 0 || variable.Any(x => !(char.IsLetterOrDigit(x) || x == '_')))
            throw new WorkflowValidationException($"{field} contains an invalid environment-variable reference.");
        var resolved = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(resolved)) throw new WorkflowValidationException($"{field} references an environment variable that is not set.");
        return resolved;
    }

    private static string ResolveProviderValue(string key, string value)
    {
        var field = $"tracker.provider.{key}";
        if (LooksSensitive(key) && !value.StartsWith('$'))
            throw new WorkflowValidationException($"{field} must use an explicit environment-variable reference.");
        return ResolveEnvironmentReference(value, field);
    }

    private static string ResolveOptionalSecret(object? value, string field)
    {
        if (value is null) return string.Empty;
        var text = StringValue(value, field)!;
        if (!text.StartsWith('$')) throw new WorkflowValidationException($"{field} must use an explicit environment-variable reference.");
        return ResolveEnvironmentReference(text, field);
    }

    private static bool LooksSensitive(string key)
        => key.Contains("token", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("password", StringComparison.OrdinalIgnoreCase)
           || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
           || key.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static void Validate(OrchestratorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Tracker.Kind)) throw new WorkflowValidationException("tracker.kind is required.");
        if (options.Tracker.ActiveProviderLifecycleStates.Count == 0) throw new WorkflowValidationException("tracker.active_states must not be empty.");
        if (options.Tracker.TerminalProviderLifecycleStates.Count == 0) throw new WorkflowValidationException("tracker.terminal_states must not be empty.");
        if (options.Retry.InitialBackoffMilliseconds > options.Retry.MaxBackoffMilliseconds)
            throw new WorkflowValidationException("retry.initial_backoff_ms must not exceed retry.max_backoff_ms.");
        if (string.IsNullOrWhiteSpace(options.Workspace.Root))
            throw new WorkflowValidationException("workspace.root is required for workspace isolation.");
        if (!Directory.Exists(options.Repository.Root))
            throw new WorkflowValidationException("repository.root must reference an existing directory.");
        if (!IsSafeRefPart(options.Repository.BaseBranch)) throw new WorkflowValidationException("repository.base_branch is invalid.");
        if (!IsSafeRefPart(options.Publication.RemoteName)) throw new WorkflowValidationException("publication.remote_name is invalid.");
        if (options.Publication.GitName.Any(char.IsControl) || options.Publication.GitEmail.Any(char.IsControl))
            throw new WorkflowValidationException("publication Git identity contains control characters.");
        if (options.Agent.GitHub.Enabled && string.IsNullOrWhiteSpace(options.Agent.GitHub.Token))
            throw new WorkflowValidationException("agent.github.token is required when agent.github.enabled is true.");
        if (!options.Agent.GitHub.Enabled && !string.IsNullOrWhiteSpace(options.Agent.GitHub.Token))
            throw new WorkflowValidationException("agent.github.token requires agent.github.enabled to be true.");
        if (options.Ci.RequiredChecks.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)))
            throw new WorkflowValidationException("ci.required_checks contains an invalid check name.");
        if (!Uri.TryCreate(options.Ci.GitHubApiBaseUrl, UriKind.Absolute, out var ciApi) || ciApi.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(ciApi.UserInfo))
            throw new WorkflowValidationException("ci.github_api_base_url must be an HTTPS origin without credentials.");
        if (!Uri.TryCreate(options.ReviewFeedback.GitHubApiBaseUrl, UriKind.Absolute, out var feedbackApi) || feedbackApi.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(feedbackApi.UserInfo))
            throw new WorkflowValidationException("review_feedback.github_api_base_url must be an HTTPS origin without credentials.");
    }

    private static bool IsSafeRefPart(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Any(char.IsControl)
           && !value.Contains("..", StringComparison.Ordinal)
           && value.IndexOfAny([' ', '~', '^', ':', '?', '*', '[', '\\']) < 0
           && !value.StartsWith('/') && !value.EndsWith('/') && !value.EndsWith('.');
}
