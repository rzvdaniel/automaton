using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public sealed class HostedRunnerRegistrationReconciler(
    ApplicationDbContext db,
    ITransactionalEventExecutor transactionExecutor,
    ILogger<HostedRunnerRegistrationReconciler> logger)
{
    public async Task<int> ReconcileAsync(DateTime now, CancellationToken cancellationToken)
    {
        var operationIds = await db.HostedRunnerDeployments
            .AsNoTracking()
            .Where(x => x.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
                && x.RegistrationDeadlineUtc != null
                && x.RegistrationDeadlineUtc <= now)
            .OrderBy(x => x.RegistrationDeadlineUtc)
            .Select(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var reconciled = 0;
        foreach (var operationId in operationIds)
        {
            var claimed = await ReconcileOperationAsync(operationId, now, cancellationToken);

            if (claimed) reconciled++;
            db.ChangeTracker.Clear();
        }

        if (reconciled > 0)
            logger.LogWarning("Failed {Count} hosted Runner operations after registration timeout", reconciled);
        return reconciled;
    }

    private async Task<bool> ReconcileOperationAsync(Guid operationId, DateTime now, CancellationToken cancellationToken)
    {
        var claimed = false;
        await transactionExecutor.ExecuteAsync(async transactionalToken =>
        {
            if (!await TryMarkDeploymentFailedAsync(operationId, now, transactionalToken)) return;
            claimed = true;
            var hostedRunner = await LoadHostedRunnerAsync(operationId, transactionalToken);
            await FailHostedRunnerAsync(hostedRunner, now, transactionalToken);
            await RevokeEnrollmentAsync(hostedRunner.Id, operationId, now, transactionalToken);
            await db.SaveChangesAsync(transactionalToken);
        }, cancellationToken);
        return claimed;
    }

    private async Task<bool> TryMarkDeploymentFailedAsync(Guid operationId, DateTime now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
            return await db.HostedRunnerDeployments
                .Where(x => x.Id == operationId
                    && x.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
                    && x.RegistrationDeadlineUtc != null && x.RegistrationDeadlineUtc <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Failed)
                    .SetProperty(x => x.Error, HostedRunnerDeploymentErrors.RegistrationTimedOut)
                    .SetProperty(x => x.FinishedAt, now), cancellationToken) == 1;

        var deployment = await db.HostedRunnerDeployments.SingleOrDefaultAsync(
            x => x.Id == operationId
                && x.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
                && x.RegistrationDeadlineUtc != null && x.RegistrationDeadlineUtc <= now,
            cancellationToken);
        if (deployment == null) return false;
        deployment.Status = HostedRunnerDeploymentStatuses.Failed;
        deployment.Error = HostedRunnerDeploymentErrors.RegistrationTimedOut;
        deployment.FinishedAt = now;
        return true;
    }

    private async Task<HostedRunner> LoadHostedRunnerAsync(Guid operationId, CancellationToken cancellationToken)
        => (await db.HostedRunnerDeployments.Include(x => x.HostedRunner)
            .SingleAsync(x => x.Id == operationId, cancellationToken)).HostedRunner;

    private async Task FailHostedRunnerAsync(HostedRunner hostedRunner, DateTime now, CancellationToken cancellationToken)
    {
        hostedRunner.Status = HostedRunnerStatuses.Failed;
        hostedRunner.LastError = HostedRunnerDeploymentErrors.RegistrationTimedOut;
        if (hostedRunner.ApiKeyId.HasValue)
        {
            var apiKey = await db.ApiKeys.SingleOrDefaultAsync(x => x.Id == hostedRunner.ApiKeyId.Value, cancellationToken);
            if (apiKey != null) apiKey.IsActive = false;
        }
        hostedRunner.ApiKeyId = null;
        hostedRunner.RunnerId = null;
        hostedRunner.ContainerId = null;
        hostedRunner.RuntimeEndpoint = null;
        hostedRunner.RuntimePort = null;
        hostedRunner.UpdatedAt = now;
    }

    private async Task RevokeEnrollmentAsync(Guid hostedRunnerId, Guid operationId, DateTime now, CancellationToken cancellationToken)
    {
        var enrollments = await db.RunnerEnrollments
            .Where(x => x.HostedRunnerId == hostedRunnerId && x.OperationId == operationId
                && (x.Status == RunnerEnrollmentStatuses.Pending || x.Status == RunnerEnrollmentStatuses.Claimed))
            .ToListAsync(cancellationToken);
        foreach (var enrollment in enrollments)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Revoked;
            enrollment.FailureReason = HostedRunnerDeploymentErrors.RegistrationTimedOut;
        }
        var secretVersionIds = enrollments.Where(x => x.SecretVersionId.HasValue)
            .Select(x => x.SecretVersionId!.Value).Distinct().ToArray();
        if (secretVersionIds.Length == 0) return;
        await RevokeSecretVersionsAsync(secretVersionIds, now, cancellationToken);
    }

    private async Task RevokeSecretVersionsAsync(Guid[] secretVersionIds, DateTime now, CancellationToken cancellationToken)
    {
        var versions = await db.SecretVersions
            .Where(x => secretVersionIds.Contains(x.Id) && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        var grants = await db.ExecutionSecretGrants
            .Where(x => secretVersionIds.Contains(x.SecretVersionId) && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var version in versions) version.RevokedAtUtc = now;
        foreach (var grant in grants) grant.RevokedAtUtc = now;
    }
}
