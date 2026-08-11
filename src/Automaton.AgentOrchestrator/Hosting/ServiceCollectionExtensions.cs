using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.AgentOrchestrator.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentOrchestrator(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IWorkflowLoader, WorkflowLoader>();
        services.AddSingleton<IWorkTracker, NoopWorkTracker>();
        services.AddSingleton<IWorkExecutor, NoopWorkExecutor>();
        services.AddSingleton<InMemoryOrchestrator>();
        services.AddHostedService<OrchestratorHostedService>();
        return services;
    }

    private sealed class NoopWorkTracker : IWorkTracker
    {
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkItem>>([]);
        public Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken) => Task.FromResult<WorkItem?>(null);
    }

    private sealed class NoopWorkExecutor : IWorkExecutor
    {
        public Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken)
            => Task.FromResult(RunOutcome.PermanentFailure("No work executor is configured."));
    }
}
