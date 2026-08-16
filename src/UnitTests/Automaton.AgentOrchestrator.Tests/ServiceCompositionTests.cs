using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class ServiceCompositionTests
{
    [Test]
    public async Task ProductionServiceGraphResolvesOnlyTheSimplifiedRuntimeAndHostedServices()
    {
        using var temp = new TempDirectory();
        var options = new OrchestratorOptions
        {
            Tracker = new TrackerOptions
            {
                Kind = "github-issues",
                Provider = new Dictionary<string, string> { ["owner"] = "acme", ["repository"] = "acceptance", ["token"] = "redacted" },
                RequiredLabels = ["agent:ready"]
            },
            Repository = new RepositoryOptions { Root = temp.Path, Identity = "acme/acceptance", BaseBranch = "main" },
            Workspace = new WorkspaceOptions { Root = Path.Combine(temp.Path, "workspaces") },
            Agent = new AgentExecutionOptions { GitHub = new AgentGitHubOptions { Enabled = true, Token = "redacted" } }
        };
        Directory.CreateDirectory(options.Workspace.Root);

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(new WorkflowDefinition(Path.Combine(temp.Path, "WORKFLOW.md"), new Dictionary<string, object?>(), "acceptance", options));
        services.AddAgentOrchestrator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IGitCommandRunner>(), Is.TypeOf<ProcessGitCommandRunner>());
            Assert.That(provider.GetRequiredService<IWorkspaceManager>(), Is.TypeOf<GitWorktreeWorkspaceManager>());
            Assert.That(provider.GetRequiredService<IWorkExecutor>(), Is.TypeOf<SupervisedCodexWorkExecutor>());
            Assert.That(services.Any(service => service.ServiceType.Name.Contains("Publisher", StringComparison.Ordinal)
                || service.ServiceType.Name.Contains("Ci", StringComparison.Ordinal)
                || service.ServiceType.Name.Contains("ReviewFeedback", StringComparison.Ordinal)), Is.False);
            Assert.That(provider.GetServices<IHostedService>(), Is.Not.Empty);
        });
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automaton-composition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
