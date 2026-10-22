using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostedRunnerRestartReconciler(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    ILogger<HostedRunnerRestartReconciler> logger)
{
    public async Task<int> ReconcileAsync(DateTime now, CancellationToken cancellationToken)
    {
        var ids = await db.HostedRunnerDeployments.AsNoTracking()
            .Where(x => x.Action == HostedRunnerDeploymentActions.Restart
                && x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect
                && x.ReconnectDeadlineUtc != null
                && x.ReconnectDeadlineUtc <= now)
            .OrderBy(x => x.ReconnectDeadlineUtc)
            .Select(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        var failed = 0;
        foreach (var id in ids)
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var claimed = await TryClaimTimedOutOperationAsync(id, now, cancellationToken);
            if (claimed != 1)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                continue;
            }

            await RecordTimedOutRestartAsync(id, now, cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            failed++;
            db.ChangeTracker.Clear();
        }
        if (failed > 0) logger.LogWarning("Failed {Count} hosted Runner restarts after reconnect timeout", failed);
        return failed;
    }

    private async Task<int> TryClaimTimedOutOperationAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            return await db.HostedRunnerDeployments
                .Where(x => x.Id == id
                    && x.Action == HostedRunnerDeploymentActions.Restart
                    && x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect
                    && x.ReconnectDeadlineUtc <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Failed)
                    .SetProperty(x => x.Error, HostedRunnerDeploymentErrors.RestartReconnectTimedOut)
                    .SetProperty(x => x.FinishedAt, now), cancellationToken);
        }

        var candidate = await db.HostedRunnerDeployments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (candidate?.Action != HostedRunnerDeploymentActions.Restart
            || candidate.Status != HostedRunnerDeploymentStatuses.WaitingForReconnect
            || candidate.ReconnectDeadlineUtc is not DateTime deadline
            || deadline > now)
            return 0;

        candidate.Status = HostedRunnerDeploymentStatuses.Failed;
        candidate.Error = HostedRunnerDeploymentErrors.RestartReconnectTimedOut;
        candidate.FinishedAt = now;
        return 1;
    }

    private async Task RecordTimedOutRestartAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        var operation = await db.HostedRunnerDeployments
            .Include(x => x.HostedRunner)
            .SingleAsync(x => x.Id == id, cancellationToken);
        ApplyRestartTimeout(operation.HostedRunner, now);
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Failed,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = operation.HostedRunnerId,
            EventType = AuditEventTypes.RunnerRestartFailed,
            Outcome = AuditOutcomes.Failed,
            Message = $"Hosted runner '{operation.HostedRunner.Name}' restart reconnect timed out",
            TargetUserId = operation.HostedRunner.UserId,
            Metadata = new { OperationId = operation.Id, Error = operation.Error },
            FailOnError = true
        }, cancellationToken);
    }

    private static void ApplyRestartTimeout(HostedRunner hostedRunner, DateTime now)
    {
        hostedRunner.Status = HostedRunnerStatuses.Failed;
        hostedRunner.LastError = HostedRunnerDeploymentErrors.RestartReconnectTimedOut;
        hostedRunner.UpdatedAt = now;
    }
}
