using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class HostedRunnerRegistrationReconcilerTests
{
    [Test]
    public async Task TimeoutRevokesEnrollmentReleasesCapacityAndPublishesExactlyOnce()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        var now = DateTime.UtcNow;
        var hostedRunnerId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var secretVersionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var retryOperationId = Guid.NewGuid();
        var retrySecretVersionId = Guid.NewGuid();
        db.HostedRunners.Add(new HostedRunner
        {
            Id = hostedRunnerId,
            UserId = Guid.NewGuid(),
            HostedRunnerSpecificationId = Guid.NewGuid(),
            Name = "timed-out-runner",
            ContainerName = "timed-out-runner",
            ContainerId = "container-id",
            RuntimeEndpoint = "http://host:20001",
            RuntimePort = 20001,
            ApiKeyId = apiKeyId,
            RunnerId = runnerId,
            Status = HostedRunnerStatuses.Provisioning
        });
        db.ApiKeys.Add(new ApiKey
        {
            Id = apiKeyId,
            Name = "pending runner credential",
            HashedKey = "hash",
            KeyPrefix = "ak_runner_test",
            CreatedAt = now,
            IsActive = true
        });
        db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
        {
            Id = operationId,
            HostedRunnerId = hostedRunnerId,
            Action = HostedRunnerDeploymentActions.Provision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            RegistrationDeadlineUtc = now.AddMinutes(-1)
        });
        db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
        {
            Id = retryOperationId,
            HostedRunnerId = hostedRunnerId,
            Action = HostedRunnerDeploymentActions.Reprovision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            RegistrationDeadlineUtc = now.AddMinutes(10)
        });
        db.RunnerEnrollments.Add(new RunnerEnrollment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunnerId,
            RunnerHostId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            ExpectedPurpose = "managed",
            EnrollmentTokenHash = Guid.NewGuid().ToString("N"),
            InstallationId = "installation",
            Status = RunnerEnrollmentStatuses.Pending,
            OperationId = operationId,
            ExpiresAtUtc = now.AddMinutes(-1),
            SecretVersionId = secretVersionId
        });
        db.RunnerEnrollments.Add(new RunnerEnrollment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunnerId, OperationId = retryOperationId,
            RunnerHostId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ExpectedPurpose = "managed",
            EnrollmentTokenHash = Guid.NewGuid().ToString("N"), InstallationId = "retry-installation",
            Status = RunnerEnrollmentStatuses.Pending, ExpiresAtUtc = now.AddMinutes(10),
            SecretVersionId = retrySecretVersionId
        });
        db.SecretVersions.Add(new SecretVersion
        {
            Id = secretVersionId,
            SecretDefinitionId = Guid.NewGuid(),
            Version = 1,
            ProtectedValue = "protected"
        });
        db.SecretVersions.Add(new SecretVersion
        {
            Id = retrySecretVersionId, SecretDefinitionId = Guid.NewGuid(), Version = 1, ProtectedValue = "retry-protected"
        });
        db.ExecutionSecretGrants.Add(new ExecutionSecretGrant
        {
            Id = Guid.NewGuid(), OperationId = operationId, SecretVersionId = secretVersionId,
            AppId = Guid.NewGuid(), ActionKey = "provision-hosted-runner", RunnerId = Guid.NewGuid(),
            DeliveryType = "environmentVariable", TargetName = "TOKEN", ExpiresAtUtc = now.AddMinutes(-1)
        });
        db.ExecutionSecretGrants.Add(new ExecutionSecretGrant
        {
            Id = Guid.NewGuid(), OperationId = retryOperationId, SecretVersionId = retrySecretVersionId,
            AppId = Guid.NewGuid(), ActionKey = "reprovision-hosted-runner", RunnerId = Guid.NewGuid(),
            DeliveryType = "environmentVariable", TargetName = "TOKEN", ExpiresAtUtc = now.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var reconciler = new HostedRunnerRegistrationReconciler(
            db, new InlineTransactionalExecutor(), publisher,
            NullLogger<HostedRunnerRegistrationReconciler>.Instance);

        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        db.ChangeTracker.Clear();

        var deployment = await db.HostedRunnerDeployments.SingleAsync(x => x.Id == operationId);
        var runner = await db.HostedRunners.SingleAsync();
        var enrollment = await db.RunnerEnrollments.SingleAsync(x => x.OperationId == operationId);
        var retryEnrollment = await db.RunnerEnrollments.SingleAsync(x => x.OperationId == retryOperationId);
        Assert.Multiple(() =>
        {
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.FinishedAt, Is.Not.Null);
            Assert.That(runner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(runner.RunnerId, Is.Null);
            Assert.That(runner.ApiKeyId, Is.Null);
            Assert.That(runner.RuntimePort, Is.Null);
            Assert.That(runner.RuntimeEndpoint, Is.Null);
            Assert.That(enrollment.Status, Is.EqualTo(RunnerEnrollmentStatuses.Revoked));
            Assert.That(retryEnrollment.Status, Is.EqualTo(RunnerEnrollmentStatuses.Pending));
        });
        Assert.That((await db.SecretVersions.SingleAsync(x => x.Id == secretVersionId)).RevokedAtUtc, Is.Not.Null);
        Assert.That((await db.SecretVersions.SingleAsync(x => x.Id == retrySecretVersionId)).RevokedAtUtc, Is.Null);
        Assert.That((await db.ExecutionSecretGrants.SingleAsync(x => x.OperationId == operationId)).RevokedAtUtc, Is.Not.Null);
        Assert.That((await db.ExecutionSecretGrants.SingleAsync(x => x.OperationId == retryOperationId)).RevokedAtUtc, Is.Null);
        Assert.That((await db.ApiKeys.SingleAsync()).IsActive, Is.False);
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionFailedEvent>().Count(), Is.EqualTo(1));
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class InlineTransactionalExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
