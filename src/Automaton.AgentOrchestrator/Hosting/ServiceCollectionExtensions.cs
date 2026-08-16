using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Trackers.GitHub;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Operator;
using Automaton.AgentOrchestrator.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.AgentOrchestrator.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentOrchestrator(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IBuildIdentity, AssemblyBuildIdentity>();
        services.AddSingleton<IGitCommandRunner, ProcessGitCommandRunner>();
        services.AddSingleton<IOrchestratorStatusQuery>(services =>
        {
            var options = services.GetRequiredService<OrchestratorOptions>();
            var path = Path.Combine(options.Workspace.Root, ".automaton-agent-orchestrator", "operator", "status.json");
            return new FileOrchestratorStatusQuery(path);
        });
        services.AddSingleton<IOperatorTelemetry>(services => services.GetRequiredService<OrchestratorOptions>().Observability.Enabled
            ? new FileOperatorTelemetry(services.GetRequiredService<OrchestratorOptions>(), services.GetRequiredService<IClock>())
            : new NoopOperatorTelemetry());
        services.AddSingleton<IWorkflowLoader, WorkflowLoader>();
        services.AddHttpClient();
        services.AddSingleton<IWorkTracker>(services =>
        {
            var options = services.GetRequiredService<OrchestratorOptions>();
            if (!string.Equals(options.Tracker.Kind, GitHubIssuesTrackerOptions.TrackerKind, StringComparison.OrdinalIgnoreCase)) return new NoopWorkTracker();
            var trackerOptions = GitHubIssuesTrackerOptions.From(options.Tracker);
            var client = services.GetRequiredService<IHttpClientFactory>().CreateClient();
            client.BaseAddress = trackerOptions.ApiBaseUri;
            return new GitHubIssuesTracker(client, trackerOptions);
        });
        services.AddSingleton<IWorkspaceManager>(services => new GitWorktreeWorkspaceManager(
            services.GetRequiredService<OrchestratorOptions>().Workspace.Root,
            services.GetRequiredService<IGitCommandRunner>(),
            services.GetRequiredService<IClock>()));
        services.AddSingleton<ICodexAppServerClientFactory, CodexAppServerClientFactory>();
        services.AddSingleton<IExecutionFence, PlatformExecutionFence>();
        services.AddSingleton<IWorkExecutor, SupervisedCodexWorkExecutor>();
        services.AddSingleton<InMemoryOrchestrator>();
        services.AddSingleton<StartupReconciler>();
        services.AddHostedService<OrchestratorHostedService>();
        services.AddHostedService<OperatorStatusHostedService>();
        return services;
    }

    private sealed class NoopWorkTracker : IWorkTracker
    {
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkItem>>([]);
        public Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken) => Task.FromResult<WorkItem?>(null);
    }
    private sealed class NoopOperatorTelemetry : IOperatorTelemetry
    {
        public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null) { }
        public OperatorEvent? LatestFor(string workItemId) => null;
        public void SetDaemonState(string state, string? error = null) { }
        public (string State, string? LastError) DaemonState() => (OperatorDaemonStates.Running, null);
    }

}
