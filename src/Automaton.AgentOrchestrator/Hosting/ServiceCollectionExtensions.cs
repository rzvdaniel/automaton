using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Trackers.GitHub;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.Ci;
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
        services.AddSingleton<IWorkspaceMutationGuard>(_ => new GitWorkspaceMutationGuard(new ProcessGitCommandRunner()));
        services.AddSingleton<ICodexAppServerClientFactory, CodexAppServerClientFactory>();
        services.AddSingleton<IExecutionFence, PlatformExecutionFence>();
        services.AddSingleton<IPullRequestPublisher>(services =>
        {
            var tracker = GitHubIssuesTrackerOptions.From(services.GetRequiredService<OrchestratorOptions>().Tracker);
            var publication = services.GetRequiredService<OrchestratorOptions>().Publication;
            if (string.IsNullOrWhiteSpace(publication.GitHubToken))
                throw new InvalidOperationException("publication.github_token is required for GitHub pull-request publication.");
            var client = services.GetRequiredService<IHttpClientFactory>().CreateClient();
            client.BaseAddress = tracker.ApiBaseUri;
            return new GitHubPullRequestPublisher(client, new(tracker.Owner, tracker.Repository, publication.GitHubToken, tracker.ApiBaseUri));
        });
        services.AddSingleton<IWorkPublisher, GitWorkPublisher>();
        services.AddSingleton<ICiProvider>(services =>
        {
            var options = services.GetRequiredService<OrchestratorOptions>();
            if (!options.Ci.Enabled) return new DisabledCiProvider();
            if (!string.Equals(options.Ci.Kind, "github", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only the GitHub CI provider is supported in Stage 10.");
            if (string.IsNullOrWhiteSpace(options.Ci.GitHubToken)) throw new InvalidOperationException("ci.github_token is required for GitHub CI observation.");
            var identity = options.Repository.Identity.Split('/');
            if (identity.Length != 2) throw new InvalidOperationException("repository.identity must be owner/repo for GitHub CI.");
            var api = new Uri(options.Ci.GitHubApiBaseUrl, UriKind.Absolute);
            var client = services.GetRequiredService<IHttpClientFactory>().CreateClient();
            client.BaseAddress = api;
            return new GitHubCiProvider(client, new(identity[0], identity[1], options.Ci.GitHubToken, api, options.Ci.RequiredChecks));
        });
        services.AddSingleton<ICiPollDelay, SystemCiPollDelay>();
        services.AddSingleton<ICiRemediator, CodexCiRemediator>();
        services.AddSingleton<ICiShepherd, CiShepherd>();
        services.AddSingleton<IWorkExecutor, RoleBasedWorkExecutor>();
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
    private sealed class DisabledCiProvider : ICiProvider
    {
        public Task<CiSnapshot> GetAsync(CiTarget target, CancellationToken cancellationToken) => Task.FromResult(new CiSnapshot(target.HeadSha, CiStatuses.Passed, []));
    }

}
