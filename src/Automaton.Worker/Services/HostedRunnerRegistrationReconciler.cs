using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public sealed class HostedRunnerRegistrationReconciler(
    ApplicationDbContext db,
    ITransactionalEventExecutor transactionExecutor,
    IEventPublisher eventPublisher,
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
            var claimed = false;
            await transactionExecutor.ExecuteAsync(async transactionalToken =>
            {
                var affected = 0;
                if (db.Database.IsRelational())
                {
                    affected = await db.HostedRunnerDeployments
                        .Where(x => x.Id == operationId
                            && x.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
                            && x.RegistrationDeadlineUtc != null
                            && x.RegistrationDeadlineUtc <= now)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Failed)
                            .SetProperty(x => x.Error, HostedRunnerDeploymentErrors.RegistrationTimedOut)
                            .SetProperty(x => x.FinishedAt, now), transactionalToken);
                }
                else
                {
                    var candidate = await db.HostedRunnerDeployments.SingleOrDefaultAsync(
                        x => x.Id == operationId
                            && x.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
                            && x.RegistrationDeadlineUtc != null
                            && x.RegistrationDeadlineUtc <= now,
                        transactionalToken);
                    if (candidate != null)
                    {
                        candidate.Status = HostedRunnerDeploymentStatuses.Failed;
                        candidate.Error = HostedRunnerDeploymentErrors.RegistrationTimedOut;
                        candidate.FinishedAt = now;
                        affected = 1;
                    }
                }
                if (affected != 1) return;

                claimed = true;
                var deployment = await db.HostedRunnerDeployments
                    .Include(x => x.HostedRunner)
                    .SingleAsync(x => x.Id == operationId, transactionalToken);
                var hostedRunner = deployment.HostedRunner;
                hostedRunner.Status = HostedRunnerStatuses.Failed;
                hostedRunner.LastError = HostedRunnerDeploymentErrors.RegistrationTimedOut;
                if (hostedRunner.ApiKeyId.HasValue)
                {
                    var apiKey = await db.ApiKeys.SingleOrDefaultAsync(
                        x => x.Id == hostedRunner.ApiKeyId.Value,
                        transactionalToken);
                    if (apiKey != null) apiKey.IsActive = false;
                }
                hostedRunner.ApiKeyId = null;
                hostedRunner.RunnerId = null;
                hostedRunner.ContainerId = null;
                hostedRunner.RuntimeEndpoint = null;
                hostedRunner.RuntimePort = null;
                hostedRunner.UpdatedAt = now;

                var enrollments = await db.RunnerEnrollments
                    .Where(x => x.HostedRunnerId == hostedRunner.Id
                        && (x.Status == RunnerEnrollmentStatuses.Pending || x.Status == RunnerEnrollmentStatuses.Claimed))
                    .ToListAsync(transactionalToken);
                var secretVersionIds = enrollments
                    .Where(x => x.SecretVersionId.HasValue)
                    .Select(x => x.SecretVersionId!.Value)
                    .Distinct()
                    .ToArray();
                foreach (var enrollment in enrollments)
                {
                    enrollment.Status = RunnerEnrollmentStatuses.Revoked;
                    enrollment.FailureReason = HostedRunnerDeploymentErrors.RegistrationTimedOut;
                }

                if (secretVersionIds.Length > 0)
                {
                    var versions = await db.SecretVersions
                        .Where(x => secretVersionIds.Contains(x.Id) && x.RevokedAtUtc == null)
                        .ToListAsync(transactionalToken);
                    var grants = await db.ExecutionSecretGrants
                        .Where(x => secretVersionIds.Contains(x.SecretVersionId) && x.RevokedAtUtc == null)
                        .ToListAsync(transactionalToken);
                    foreach (var version in versions) version.RevokedAtUtc = now;
                    foreach (var grant in grants) grant.RevokedAtUtc = now;
                }

                await db.SaveChangesAsync(transactionalToken);
                await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
                {
                    HostedRunnerId = hostedRunner.Id,
                    UserId = hostedRunner.UserId,
                    Error = HostedRunnerDeploymentErrors.RegistrationTimedOut,
                    CorrelationId = $"hosted-runner-registration-timeout:{operationId:N}"
                }, transactionalToken);
            }, cancellationToken);

            if (claimed) reconciled++;
            db.ChangeTracker.Clear();
        }

        if (reconciled > 0)
            logger.LogWarning("Failed {Count} hosted Runner operations after registration timeout", reconciled);
        return reconciled;
    }
}
