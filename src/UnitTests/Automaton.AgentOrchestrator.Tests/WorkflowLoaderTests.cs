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
                workspace:
                  root: workspaces
                agent:
                  max_concurrent_agents: 2
                  max_turns: 5
                  manager_max_turns: 2
                  developer_max_turns: 6
                  reviewer_max_turns: 3
                codex:
                  command: codex app-server
                  turn_timeout_ms: 99
                retry:
                  max_attempts: 4
                  initial_backoff_ms: 3
                  max_backoff_ms: 9
                reconciliation:
                  interval_ms: 17
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
                Assert.That(definition.Options.Workspace.Root, Is.EqualTo(Path.Combine(workflow.Directory, "workspaces")));
                Assert.That(definition.Options.Agent.MaxConcurrentAgents, Is.EqualTo(2));
                Assert.That(definition.Options.Agent.ManagerMaxTurns, Is.EqualTo(2));
                Assert.That(definition.Options.Agent.DeveloperMaxTurns, Is.EqualTo(6));
                Assert.That(definition.Options.Agent.ReviewerMaxTurns, Is.EqualTo(3));
                Assert.That(definition.Options.Retry.MaxAttempts, Is.EqualTo(4));
                Assert.That(definition.Options.Reconciliation.IntervalMilliseconds, Is.EqualTo(17));
            });
        }
        finally { Environment.SetEnvironmentVariable(variableName, null); }
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
