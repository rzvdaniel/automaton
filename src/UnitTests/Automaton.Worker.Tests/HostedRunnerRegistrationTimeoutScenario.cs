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

internal sealed class HostedRunnerRegistrationTimeoutScenario : IAsyncDisposable
{
    private readonly ApplicationDbContext db;
    private readonly RecordingPublisher publisher;
    private readonly HostedRunnerRegistrationReconciler reconciler;
    private readonly Guid operationId;
    private readonly Guid retryOperationId;
    private readonly Guid secretVersionId;
    private readonly Guid retrySecretVersionId;

    private HostedRunnerRegistrationTimeoutScenario(
        ApplicationDbContext db,
        RecordingPublisher publisher,
        HostedRunnerRegistrationReconciler reconciler,
        Guid operationId,
        Guid retryOperationId,
        Guid secretVersionId,
        Guid retrySecretVersionId)
    {
        this.db = db;
        this.publisher = publisher;
        this.reconciler = reconciler;
        this.operationId = operationId;
        this.retryOperationId = retryOperationId;
        this.secretVersionId = secretVersionId;
        this.retrySecretVersionId = retrySecretVersionId;
    }

    internal static async Task<HostedRunnerRegistrationTimeoutScenario> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var now = DateTime.UtcNow;
        var hostedRunnerId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var retryOperationId = Guid.NewGuid();
        var secretVersionId = Guid.NewGuid();
        var retrySecretVersionId = Guid.NewGuid();
        AddRunner(db, hostedRunnerId, apiKeyId);
        AddApiKey(db, apiKeyId, now);
        AddDeployments(db, hostedRunnerId, operationId, retryOperationId, now);
        AddEnrollments(db, hostedRunnerId, operationId, retryOperationId, secretVersionId, retrySecretVersionId, now);
        AddSecretsAndGrants(db, operationId, retryOperationId, secretVersionId, retrySecretVersionId, now);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var reconciler = new HostedRunnerRegistrationReconciler(
            db, new InlineTransactionalExecutor(), publisher,
            NullLogger<HostedRunnerRegistrationReconciler>.Instance);
        return new HostedRunnerRegistrationTimeoutScenario(
            db, publisher, reconciler, operationId, retryOperationId, secretVersionId, retrySecretVersionId);
    }

    internal async Task ReconcileAndAssertAsync()
    {
        var now = DateTime.UtcNow;
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        db.ChangeTracker.Clear();
        await AssertDeploymentAndRunnerAsync();
        await AssertEnrollmentsAsync();
        await AssertSecretsAndGrantsAsync();
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionFailedEvent>().Count(), Is.EqualTo(1));
    }

    private static void AddRunner(ApplicationDbContext db, Guid runnerId, Guid apiKeyId)
        => db.HostedRunners.Add(new HostedRunner
        {
            Id = runnerId, UserId = Guid.NewGuid(), HostedRunnerSpecificationId = Guid.NewGuid(),
            Name = "timed-out-runner", ContainerName = "timed-out-runner", ContainerId = "container-id",
            RuntimeEndpoint = "http://host:20001", RuntimePort = 20001, ApiKeyId = apiKeyId,
            RunnerId = Guid.NewGuid(), Status = HostedRunnerStatuses.Provisioning
        });

    private static void AddApiKey(ApplicationDbContext db, Guid apiKeyId, DateTime now)
        => db.ApiKeys.Add(new ApiKey
        {
            Id = apiKeyId, Name = "pending runner credential", HashedKey = "hash",
            KeyPrefix = "ak_runner_test", CreatedAt = now, IsActive = true
        });

    private static void AddDeployments(ApplicationDbContext db, Guid runnerId, Guid operationId, Guid retryOperationId, DateTime now)
    {
        db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
        {
            Id = operationId, HostedRunnerId = runnerId, Action = HostedRunnerDeploymentActions.Provision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration, RegistrationDeadlineUtc = now.AddMinutes(-1)
        });
        db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
        {
            Id = retryOperationId, HostedRunnerId = runnerId, Action = HostedRunnerDeploymentActions.Reprovision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration, RegistrationDeadlineUtc = now.AddMinutes(10)
        });
    }

    private static void AddEnrollments(
        ApplicationDbContext db, Guid runnerId, Guid operationId, Guid retryOperationId,
        Guid secretVersionId, Guid retrySecretVersionId, DateTime now)
    {
        db.RunnerEnrollments.Add(CreateEnrollment(runnerId, operationId, secretVersionId, now.AddMinutes(-1)));
        db.RunnerEnrollments.Add(CreateEnrollment(runnerId, retryOperationId, retrySecretVersionId, now.AddMinutes(10)));
    }

    private static RunnerEnrollment CreateEnrollment(Guid runnerId, Guid operationId, Guid secretVersionId, DateTime expiresAt)
        => new()
        {
            Id = Guid.NewGuid(), HostedRunnerId = runnerId, OperationId = operationId, RunnerHostId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(), ExpectedPurpose = "managed", EnrollmentTokenHash = Guid.NewGuid().ToString("N"),
            InstallationId = operationId.ToString("N"), Status = RunnerEnrollmentStatuses.Pending,
            ExpiresAtUtc = expiresAt, SecretVersionId = secretVersionId
        };

    private static void AddSecretsAndGrants(
        ApplicationDbContext db, Guid operationId, Guid retryOperationId,
        Guid secretVersionId, Guid retrySecretVersionId, DateTime now)
    {
        db.SecretVersions.Add(new SecretVersion
        {
            Id = secretVersionId, SecretDefinitionId = Guid.NewGuid(), Version = 1, ProtectedValue = "protected"
        });
        db.SecretVersions.Add(new SecretVersion
        {
            Id = retrySecretVersionId, SecretDefinitionId = Guid.NewGuid(), Version = 1, ProtectedValue = "retry-protected"
        });
        db.ExecutionSecretGrants.Add(CreateGrant(operationId, secretVersionId, now.AddMinutes(-1)));
        db.ExecutionSecretGrants.Add(CreateGrant(retryOperationId, retrySecretVersionId, now.AddMinutes(10)));
    }

    private static ExecutionSecretGrant CreateGrant(Guid operationId, Guid secretVersionId, DateTime expiresAt)
        => new()
        {
            Id = Guid.NewGuid(), OperationId = operationId, SecretVersionId = secretVersionId,
            AppId = Guid.NewGuid(), ActionKey = "provision-hosted-runner", RunnerId = Guid.NewGuid(),
            DeliveryType = "environmentVariable", TargetName = "TOKEN", ExpiresAtUtc = expiresAt
        };

    private async Task AssertDeploymentAndRunnerAsync()
    {
        var deployment = await db.HostedRunnerDeployments.SingleAsync(x => x.Id == operationId);
        var runner = await db.HostedRunners.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.FinishedAt, Is.Not.Null);
            Assert.That(runner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(runner.RunnerId, Is.Null);
            Assert.That(runner.ApiKeyId, Is.Null);
            Assert.That(runner.RuntimePort, Is.Null);
            Assert.That(runner.RuntimeEndpoint, Is.Null);
            Assert.That(db.ApiKeys.Single().IsActive, Is.False);
        });
    }

    private async Task AssertEnrollmentsAsync()
    {
        var enrollment = await db.RunnerEnrollments.SingleAsync(x => x.OperationId == operationId);
        var retryEnrollment = await db.RunnerEnrollments.SingleAsync(x => x.OperationId == retryOperationId);
        Assert.Multiple(() =>
        {
            Assert.That(enrollment.Status, Is.EqualTo(RunnerEnrollmentStatuses.Revoked));
            Assert.That(retryEnrollment.Status, Is.EqualTo(RunnerEnrollmentStatuses.Pending));
        });
    }

    private async Task AssertSecretsAndGrantsAsync()
    {
        var secret = await db.SecretVersions.SingleAsync(x => x.Id == secretVersionId);
        var retrySecret = await db.SecretVersions.SingleAsync(x => x.Id == retrySecretVersionId);
        var grant = await db.ExecutionSecretGrants.SingleAsync(x => x.OperationId == operationId);
        var retryGrant = await db.ExecutionSecretGrants.SingleAsync(x => x.OperationId == retryOperationId);
        Assert.Multiple(() =>
        {
            Assert.That(secret.RevokedAtUtc, Is.Not.Null);
            Assert.That(retrySecret.RevokedAtUtc, Is.Null);
            Assert.That(grant.RevokedAtUtc, Is.Not.Null);
            Assert.That(retryGrant.RevokedAtUtc, Is.Null);
        });
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();

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
