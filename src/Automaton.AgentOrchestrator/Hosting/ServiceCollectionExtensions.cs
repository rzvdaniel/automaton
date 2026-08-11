using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Trackers.GitHub;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Codex;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.AgentOrchestrator.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentOrchestrator(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
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
            new ProcessGitCommandRunner(),
            services.GetRequiredService<IClock>()));
        services.AddSingleton<ICodexAppServerClientFactory, CodexAppServerClientFactory>();
        services.AddSingleton<IExecutionFence, PlatformExecutionFence>();
        services.AddSingleton<IWorkExecutor, SingleAgentWorkExecutor>();
        services.AddSingleton<InMemoryOrchestrator>();
        services.AddSingleton<StartupReconciler>();
        services.AddHostedService<OrchestratorHostedService>();
        return services;
    }

    private sealed class NoopWorkTracker : IWorkTracker
    {
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkItem>>([]);
        public Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken) => Task.FromResult<WorkItem?>(null);
    }

}
