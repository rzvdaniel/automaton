using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Hosting;

public sealed class OrchestratorHostedService(InMemoryOrchestrator orchestrator, OrchestratorOptions options, ILogger<OrchestratorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Polling.IntervalMilliseconds));
        do
        {
            try { await orchestrator.PollAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Agent orchestrator polling failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
