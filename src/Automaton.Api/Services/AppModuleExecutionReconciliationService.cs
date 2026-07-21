using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Marks unfinished MVP dispatches lost after an API restart or ungraceful disconnect.</summary>
public sealed class AppModuleExecutionReconciliationService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory scopeFactory;

    public AppModuleExecutionReconciliationService(IServiceScopeFactory scopeFactory) => this.scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileAsync(stoppingToken);
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReconcileAsync(stoppingToken);
    }

    internal async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTime.UtcNow - StaleAfter;
        var stale = await db.AppModuleExecutions
            .Where(execution =>
                (execution.Status == AppModuleExecutionStatuses.Dispatching && execution.CreatedAtUtc < cutoff)
                || (execution.Status == AppModuleExecutionStatuses.Running
                    && (execution.StartedAtUtc == null || execution.StartedAtUtc < cutoff)))
            .ToListAsync(cancellationToken);

        foreach (var execution in stale)
        {
            execution.Status = AppModuleExecutionStatuses.Lost;
            execution.ErrorCode = AppModuleExecutionErrorCodes.RunnerUnavailable;
            execution.FailureSummary = "Execution was not confirmed before the API reconciliation timeout.";
            execution.CompletedAtUtc = DateTime.UtcNow;
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
