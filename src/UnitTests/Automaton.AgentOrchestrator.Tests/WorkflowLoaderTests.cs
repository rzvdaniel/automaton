using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class WorkflowLoaderTests
{
    [Test]
    public async Task LoadAsync_ParsesFrontMatterPromptAndEnvironmentReferences()
    {
        var variableName = "AUTOMATON_ORCHESTRATOR_TEST_TOKEN";
        Environment.SetEnvironmentVariable(variableName, "test-value");
        Environment.SetEnvironmentVariable("AUTOMATON_ORCHESTRATOR_AGENT_TOKEN", "agent-value");
        try
        {
            await using var workflow = await TemporaryWorkflow.CreateAsync("""
                ---
                tracker:
                  kind: fake
                  provider:
                    token: $AUTOMATON_ORCHESTRATOR_TEST_TOKEN
                  required_labels:
                    - agent:ready
                polling:
                  interval_ms: 42
                repository:
                  root: .
                  identity: acme/widgets
                  base_branch: trunk
                workspace:
                  root: workspaces
                agent:
                  max_concurrent_agents: 2
                  max_turns: 5
                  github:
                    enabled: true
                    token: $AUTOMATON_ORCHESTRATOR_AGENT_TOKEN
                  profiles:
                    developer:
                      name: developer
                      model: model-a
                      workspace_access: read-write
                      github: write
                      instructions: Use the developer profile.
                    reviewer:
                      name: reviewer
                      model: model-b
                      workspace_access: read-only
                      github: read-only
                      instructions: Review without modifying.
                review:
                  enabled: true
                codex:
                  command: codex app-server
                  turn_timeout_ms: 99
                  stall_timeout_ms: 37
                retry:
                  max_attempts: 4
                  initial_backoff_ms: 3
                  max_backoff_ms: 9
                reconciliation:
                  interval_ms: 17
                observability:
                  enabled: true
                  snapshot_interval_ms: 250
                  maximum_event_file_bytes: 4096
                ---
                # Engineering task
                Work on {{ issue.identifier }}.
                """);

            var definition = await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(definition.PromptTemplate, Is.EqualTo("# Engineering task\nWork on {{ issue.identifier }}."));
                Assert.That(definition.Options.Tracker.Kind, Is.EqualTo("fake"));
                Assert.That(definition.Options.Tracker.Provider["token"], Is.EqualTo("test-value"));
                Assert.That(definition.Options.Tracker.RequiredLabels, Is.EqualTo(new[] { "agent:ready" }));
                Assert.That(definition.Options.Polling.IntervalMilliseconds, Is.EqualTo(42));
                Assert.That(definition.Options.Repository.Root, Is.EqualTo(workflow.Directory));
                Assert.That(definition.Options.Repository.Identity, Is.EqualTo("acme/widgets"));
                Assert.That(definition.Options.Repository.BaseBranch, Is.EqualTo("trunk"));
                Assert.That(definition.Options.Workspace.Root, Is.EqualTo(Path.Combine(workflow.Directory, "workspaces")));
                Assert.That(definition.Options.Agent.MaxConcurrentAgents, Is.EqualTo(2));
                Assert.That(definition.Options.Agent.GitHub.Enabled, Is.True);
                Assert.That(definition.Options.Agent.GitHub.Token, Is.EqualTo("agent-value"));
                Assert.That(definition.Options.Agent.Developer, Is.EqualTo(new AgentExecutionProfile("developer", "model-a", "read-write", "write", "Use the developer profile.")));
                Assert.That(definition.Options.Agent.Reviewer, Is.EqualTo(new AgentExecutionProfile("reviewer", "model-b", "read-only", "read-only", "Review without modifying.")));
                Assert.That(definition.Options.Agent.Review.Enabled, Is.True);
                Assert.That(definition.Options.Retry.MaxAttempts, Is.EqualTo(4));
                Assert.That(definition.Options.Reconciliation.IntervalMilliseconds, Is.EqualTo(17));
                Assert.That(definition.Options.Observability.SnapshotIntervalMilliseconds, Is.EqualTo(250));
                Assert.That(definition.Options.Observability.MaximumEventFileBytes, Is.EqualTo(4096));
                Assert.That(definition.Options.Codex.TurnTimeoutMilliseconds, Is.EqualTo(99));
                Assert.That(definition.Options.Codex.StallTimeoutMilliseconds, Is.EqualTo(37));
            });
        }
        finally { Environment.SetEnvironmentVariable(variableName, null); Environment.SetEnvironmentVariable("AUTOMATON_ORCHESTRATOR_AGENT_TOKEN", null); }
    }

    [Test]
    public async Task LoadAsync_RejectsMissingEnvironmentReferenceWithoutLeakingValue()
    {
        const string variableName = "AUTOMATON_ORCHESTRATOR_MISSING_SECRET";
        Environment.SetEnvironmentVariable(variableName, null);
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
              provider:
                token: $AUTOMATON_ORCHESTRATOR_MISSING_SECRET
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () =>
            await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("environment variable that is not set"));
        Assert.That(exception.Message, Does.Not.Contain("$AUTOMATON_ORCHESTRATOR_MISSING_SECRET"));
    }

    [Test]
    public async Task LoadAsync_RequiresAnExplicitCredentialForEnabledAgentGitHubCapability()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            workspace:
              root: workspaces
            agent:
              github:
                enabled: true
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(() => new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));
        Assert.That(exception!.Message, Is.EqualTo("agent.github.token is required when agent.github.enabled is true."));
    }

    [Test]
    public async Task LoadAsync_RejectsRemovedPublicationCiAndFeedbackConfiguration()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            workspace:
              root: workspaces
            ci:
              enabled: true
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(() => new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));
        Assert.That(exception!.Message, Does.StartWith("ci is no longer orchestrator configuration"));
    }

    [Test]
    public async Task LoadAsync_RejectsInvalidConfiguration()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            retry:
              initial_backoff_ms: 20
              max_backoff_ms: 10
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () =>
            await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("retry.initial_backoff_ms must not exceed retry.max_backoff_ms."));
    }

    [Test]
    public async Task LoadAsync_RejectsInvalidStallTimeout()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            codex:
              stall_timeout_ms: 0
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(() => new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("codex.stall_timeout_ms must be a positive integer."));
    }

    [Test]
    public async Task LoadAsync_RejectsLiteralSensitiveProviderConfiguration()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
              provider:
                api_token: literal-value
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () =>
            await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("tracker.provider.api_token must use an explicit environment-variable reference."));
            Assert.That(exception.Message, Does.Not.Contain("literal-value"));
        });
    }

    [Test]
    public async Task LoadAsync_RequiresWorkspaceRootForIsolation()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () =>
            await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("workspace.root is required for workspace isolation."));
    }

    [Test]
    public async Task LoadAsync_RejectsMissingCanonicalRepositoryRoot()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            repository:
              root: missing-repository
              identity: acme/widgets
            workspace:
              root: workspaces
            ---
            Prompt
            """);

        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () =>
            await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("repository.root must reference an existing directory."));
    }

    [TestCase("banana", "read-write", "agent.profiles.developer.workspace_access must be read-write or read-only.")]
    [TestCase("read-only", "arbitrary", "agent.profiles.developer.github must be write, read-only, or none.")]
    public async Task LoadAsync_RejectsInvalidProfileValues(string workspaceAccess, string github, string message)
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync($"""
            ---
            tracker:
              kind: fake
            repository:
              root: .
            agent:
              profiles:
                developer:
                  workspace_access: {workspaceAccess}
                  github: {github}
            ---
            Prompt
            """);
        var exception = Assert.ThrowsAsync<WorkflowValidationException>(async () => await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None));
        Assert.That(exception!.Message, Is.EqualTo(message));
    }

    [Test]
    public async Task LoadAsync_UsesSafeRoleSpecificDefaultsWhenProfilesAreOmitted()
    {
        await using var workflow = await TemporaryWorkflow.CreateAsync("""
            ---
            tracker:
              kind: fake
            repository:
              root: .
            workspace:
              root: workspaces
            review:
              enabled: true
            ---
            Prompt
            """);
        var options = (await new WorkflowLoader().LoadAsync(workflow.Path, CancellationToken.None)).Options;
        Assert.Multiple(() =>
        {
            Assert.That(options.Agent.Developer, Is.EqualTo(AgentExecutionProfile.DefaultDeveloper));
            Assert.That(options.Agent.Reviewer, Is.EqualTo(AgentExecutionProfile.DefaultReviewer));
            Assert.That(options.Agent.Review.Enabled, Is.True);
            Assert.That(options.Agent.Reviewer.WorkspaceAccess, Is.EqualTo("read-only"));
            Assert.That(options.Agent.Reviewer.GitHubAccess, Is.EqualTo("read-only"));
        });
    }

    private sealed class TemporaryWorkflow : IAsyncDisposable
    {
        private TemporaryWorkflow(string directory, string path) { Directory = directory; Path = path; }
        public string Directory { get; }
        public string Path { get; }

        public static async Task<TemporaryWorkflow> CreateAsync(string content)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"automaton-orchestrator-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "WORKFLOW.md");
            await File.WriteAllTextAsync(path, content);
            return new TemporaryWorkflow(directory, path);
        }

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
