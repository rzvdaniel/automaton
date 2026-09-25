using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Confirms Hosted Runner restarts only after a distinct, post-request Runner connection is observed.
/// </summary>
public sealed class HostedRunnerRestartCompletionService(
    ApplicationDbContext dbContext,
    IAuditEventService auditEventService)
{
    private sealed record PendingHostedRunnerRestart(
        Guid Id,
        Guid HostedRunnerId,
        string Status,
        string? PreviousConnectionId,
        DateTime? ReconnectAfterUtc,
        string Name,
        Guid UserId);

    public async Task CompleteAsync(Runner runner, CancellationToken cancellationToken)
    {
        var operation = await GetPendingRestartAsync(runner.Id, cancellationToken);
        if (operation == null || string.IsNullOrWhiteSpace(runner.ConnectionId))
            return;
        if (string.Equals(runner.ConnectionId, operation.PreviousConnectionId, StringComparison.Ordinal))
            return;

        if (!await HasNewRunnerConnectionAsync(runner.Id, runner.ConnectionId, operation.ReconnectAfterUtc, cancellationToken))
            return;

        await CompleteAtomicallyAsync(operation, runner.Id, DateTime.UtcNow, cancellationToken);
    }

    private Task<PendingHostedRunnerRestart?> GetPendingRestartAsync(Guid runnerId, CancellationToken cancellationToken)
        => dbContext.HostedRunnerDeployments
            .Where(x => x.Action == HostedRunnerDeploymentActions.Restart
                && x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect
                && x.ExpectedRunnerId == runnerId
                && x.ReconnectAfterUtc != null
                && x.ReconnectDeadlineUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new PendingHostedRunnerRestart(
                x.Id,
                x.HostedRunnerId,
                x.Status,
                x.PreviousConnectionId,
                x.ReconnectAfterUtc,
                x.HostedRunner.Name,
                x.HostedRunner.UserId))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> HasNewRunnerConnectionAsync(
        Guid runnerId,
        string connectionId,
        DateTime? reconnectAfterUtc,
        CancellationToken cancellationToken)
        => dbContext.RunnerConnectionSessions.AnyAsync(x =>
            x.RunnerId == runnerId
            && x.ConnectionId == connectionId
            && x.ConnectedAtUtc > reconnectAfterUtc,
            cancellationToken);

    private async Task CompleteAtomicallyAsync(
        PendingHostedRunnerRestart operation,
        Guid runnerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var completed = await PersistCompletionAsync(operation, now, cancellationToken);
            if (!completed)
            {
                if (transaction != null)
                    await transaction.RollbackAsync(cancellationToken);
                return;
            }

            await WriteRestartAuditAsync(operation, runnerId, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<bool> PersistCompletionAsync(
        PendingHostedRunnerRestart operation,
        DateTime now,
        CancellationToken cancellationToken)
        => dbContext.Database.IsRelational()
            ? PersistRelationalCompletionAsync(operation, now, cancellationToken)
            : PersistTrackedCompletionAsync(operation, now, cancellationToken);

    private async Task<bool> PersistTrackedCompletionAsync(
        PendingHostedRunnerRestart operation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var trackedOperation = await dbContext.HostedRunnerDeployments.SingleAsync(
            x => x.Id == operation.Id,
            cancellationToken);
        if (trackedOperation.Status != HostedRunnerDeploymentStatuses.WaitingForReconnect)
            return false;

        trackedOperation.Status = HostedRunnerDeploymentStatuses.Succeeded;
        trackedOperation.FinishedAt = now;
        trackedOperation.Error = null;
        trackedOperation.Logs = "Restart confirmed by a new Runner connection and heartbeat.";
        var hostedRunner = await dbContext.HostedRunners.SingleAsync(
            x => x.Id == operation.HostedRunnerId,
            cancellationToken);
        hostedRunner.Status = HostedRunnerStatuses.Running;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> PersistRelationalCompletionAsync(
        PendingHostedRunnerRestart operation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var completed = await dbContext.HostedRunnerDeployments
            .Where(x => x.Id == operation.Id
                && x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect
                && x.ReconnectDeadlineUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Succeeded)
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.Error, (string?)null)
                .SetProperty(x => x.Logs, "Restart confirmed by a new Runner connection and heartbeat."),
                cancellationToken);
        if (completed != 1)
            return false;

        var hostedRunnerUpdated = await dbContext.HostedRunners
            .Where(x => x.Id == operation.HostedRunnerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HostedRunnerStatuses.Running)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);
        if (hostedRunnerUpdated != 1)
            throw new InvalidOperationException("The hosted Runner restart state could not be completed.");
        return true;
    }

    private Task WriteRestartAuditAsync(
        PendingHostedRunnerRestart operation,
        Guid runnerId,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = operation.HostedRunnerId,
            EventType = AuditEventTypes.RunnerRestartSucceeded,
            Outcome = AuditOutcomes.Success,
            Message = $"Hosted runner '{operation.Name}' restart was confirmed by heartbeat",
            TargetUserId = operation.UserId,
            Metadata = new { OperationId = operation.Id, RunnerId = runnerId },
            FailOnError = true
        }, cancellationToken);
}
