using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Hosting;

public sealed class OrchestratorHostedService(InMemoryOrchestrator orchestrator, StartupReconciler reconciler, OrchestratorOptions options, ILogger<OrchestratorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!orchestrator.IsStartupReconciled)
        {
            try { await reconciler.ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Startup reconciliation failed; polling remains disabled");
                await Task.Delay(TimeSpan.FromMilliseconds(options.Reconciliation.IntervalMilliseconds), stoppingToken);
            }
        }
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
