using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class HostedRunnerLifecycleReconcilerTests
{
    [Test]
    public async Task StaleQueuedRestartIsNotRedispatchedWithoutExecutionRecord()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (_, operation) = SeedOperation(db, HostedRunnerDeploymentActions.Restart, HostedRunnerDeploymentStatuses.Queued, now);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var reconciler = CreateReconciler(db, publisher);

        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        Assert.That(publisher.Messages, Is.Empty);
        var recovered = await db.HostedRunnerDeployments.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        Assert.That(recovered.LastRecoveryAttemptUtc, Is.Null);
    }

    [Test]
    public async Task StaleStorageVerificationIsLeftToStorageReconciliation()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (_, operation) = SeedOperation(db, HostedRunnerDeploymentActions.VerifyStorage, HostedRunnerDeploymentStatuses.Pending, now);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();

        Assert.That(await CreateReconciler(db, publisher).ReconcileAsync(now, CancellationToken.None), Is.Zero);
        Assert.That(publisher.Messages, Is.Empty);
        Assert.That((await db.HostedRunnerDeployments.SingleAsync(x => x.Id == operation.Id)).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Pending));
    }

    private static (HostedRunner Runner, HostedRunnerDeployment Operation) SeedOperation(
        ApplicationDbContext db, string action, string status, DateTime now)
    {
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid() };
        var runner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerHostId = host.Id, RunnerHost = host,
            Name = "managed", ContainerName = "managed", Status = HostedRunnerStatuses.Provisioning
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id, Action = action, Status = status,
            StartedAt = now.AddMinutes(-10)
        };
        runner.Deployments.Add(operation);
        db.AddRange(host, runner);
        return (runner, operation);
    }

    private static HostedRunnerLifecycleReconciler CreateReconciler(
        ApplicationDbContext db,
        RecordingPublisher publisher,
        IAuditEventService? auditEventService = null) =>
        new(db, new InlineTransactionalExecutor(), publisher, auditEventService ?? new NoOpAuditService(),
            new OptionsSnapshot(new HostedRunnerOptions
            {
                LifecycleRecoveryDelaySeconds = 60, LifecycleRecoveryRetrySeconds = 300
            }), NullLogger<HostedRunnerLifecycleReconciler>.Instance);

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class InlineTransactionalExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
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

    private sealed class NoOpAuditService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OptionsSnapshot(HostedRunnerOptions value) : IOptionsSnapshot<HostedRunnerOptions>
    {
        public HostedRunnerOptions Value => value;
        public HostedRunnerOptions Get(string? name) => value;
    }
}
