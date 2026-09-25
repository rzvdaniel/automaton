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

public sealed class HostedRunnerLifecycleRecoveryTests
{
    [Test]
    public async Task QueuedReprovisionWithoutExecutionIsNotRepublished()
    {
        var result = await ReconcileMissingExecutionScenarioAsync(
            HostedRunnerDeploymentActions.Reprovision,
            HostedRunnerDeploymentStatuses.Queued,
            reconcileTwice: true);
        Assert.Multiple(() =>
        {
            Assert.That(result.FirstPass, Is.Zero);
            Assert.That(result.SecondPass, Is.Zero);
            Assert.That(result.ProvisionEvents, Is.Zero);
            Assert.That(result.ModuleExecutions, Is.Zero);
            Assert.That(result.OperationStatus, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
            Assert.That(result.RunnerStatus, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        });
    }

    [Test]
    public async Task StaleExecutingProvisionWithoutExecutionIsNotRepublished()
    {
        var result = await ReconcileMissingExecutionScenarioAsync(
            HostedRunnerDeploymentActions.Provision,
            HostedRunnerDeploymentStatuses.Executing,
            reconcileTwice: false);
        Assert.Multiple(() =>
        {
            Assert.That(result.FirstPass, Is.Zero);
            Assert.That(result.ProvisionEvents, Is.Zero);
            Assert.That(result.OperationStatus, Is.EqualTo(HostedRunnerDeploymentStatuses.Executing));
        });
    }

    [Test]
    public async Task TerminalExecutionWithoutAppliedLifecycleResultFailsAmbiguously()
    {
        var result = await ReconcileTerminalExecutionScenarioAsync(AppModuleExecutionStatuses.Succeeded);
        Assert.Multiple(() =>
        {
            Assert.That(result.FirstPass, Is.EqualTo(1));
            Assert.That(result.OperationStatus, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(result.Error, Is.EqualTo(HostedRunnerDeploymentErrors.LifecycleResultAmbiguous));
            Assert.That(result.RunnerStatus, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(result.FailureEvents, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RunnerUnavailableExecutionFailsLifecycleOperationExactlyOnce()
    {
        var result = await ReconcileTerminalExecutionScenarioAsync(
            AppModuleExecutionStatuses.RunnerUnavailable,
            "Infrastructure Runner unavailable.");
        Assert.Multiple(() =>
        {
            Assert.That(result.FirstPass, Is.EqualTo(1));
            Assert.That(result.SecondPass, Is.Zero);
            Assert.That(result.OperationStatus, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(result.Error, Is.EqualTo("Infrastructure Runner unavailable."));
            Assert.That(result.RunnerStatus, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(result.FailureEvents, Is.EqualTo(1));
            Assert.That(result.AuditEvents, Is.EqualTo(1));
        });
    }

    private static async Task<MissingExecutionResult> ReconcileMissingExecutionScenarioAsync(
        string action,
        string status,
        bool reconcileTwice)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (runner, operation) = SeedOperation(db, action, status, now);
        db.ExecutionSecretGrants.Add(CreateGrant(operation.Id, $"{action}-hosted-runner", now));
        await db.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var reconciler = CreateReconciler(db, publisher);
        var firstPass = await reconciler.ReconcileAsync(now, CancellationToken.None);
        var secondPass = reconcileTwice
            ? await reconciler.ReconcileAsync(now, CancellationToken.None)
            : 0;
        var recovered = await db.HostedRunnerDeployments.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        var currentRunner = await db.HostedRunners.AsNoTracking().SingleAsync(x => x.Id == runner.Id);

        return new MissingExecutionResult(
            firstPass,
            secondPass,
            publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>().Count(),
            await db.AppModuleExecutions.CountAsync(),
            recovered.Status,
            currentRunner.Status,
            recovered.LastRecoveryAttemptUtc.HasValue);
    }

    private static async Task<TerminalExecutionResult> ReconcileTerminalExecutionScenarioAsync(
        string executionStatus,
        string? failureSummary = null)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (runner, operation) = SeedOperation(
            db, HostedRunnerDeploymentActions.Provision, HostedRunnerDeploymentStatuses.Executing, now);
        db.AppModuleExecutions.Add(CreateExecution(operation.Id, now, executionStatus, failureSummary));
        await db.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var audit = new RecordingAuditService();
        var reconciler = CreateReconciler(db, publisher, audit);
        var firstPass = await reconciler.ReconcileAsync(now, CancellationToken.None);
        var secondPass = await reconciler.ReconcileAsync(now, CancellationToken.None);
        var failedOperation = await db.HostedRunnerDeployments.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        var failedRunner = await db.HostedRunners.AsNoTracking().SingleAsync(x => x.Id == runner.Id);

        return new TerminalExecutionResult(
            firstPass,
            secondPass,
            failedOperation.Status,
            failedOperation.Error,
            failedRunner.Status,
            publisher.Messages.OfType<ManagedRunnerProvisionFailedEvent>().Count(),
            audit.Requests.Count);
    }

    private static (HostedRunner Runner, HostedRunnerDeployment Operation) SeedOperation(
        ApplicationDbContext db,
        string action,
        string status,
        DateTime now)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host",
            Provider = "test",
            InfrastructureRunnerId = Guid.NewGuid()
        };
        var runner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            RunnerHostId = host.Id,
            RunnerHost = host,
            Name = "managed",
            ContainerName = "managed",
            Status = HostedRunnerStatuses.Provisioning
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = runner.Id,
            Action = action,
            Status = status,
            StartedAt = now.AddMinutes(-10)
        };
        runner.Deployments.Add(operation);
        db.AddRange(host, runner);
        return (runner, operation);
    }

    private static ExecutionSecretGrant CreateGrant(Guid operationId, string actionKey, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        OperationId = operationId,
        SecretVersionId = Guid.NewGuid(),
        AppId = Guid.NewGuid(),
        ActionKey = actionKey,
        RunnerId = Guid.NewGuid(),
        DeliveryType = "environmentVariable",
        TargetName = "TOKEN",
        ExpiresAtUtc = now.AddMinutes(10)
    };

    private static AppModuleExecution CreateExecution(
        Guid operationId,
        DateTime now,
        string status,
        string? failureSummary = null) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerOperationId = operationId,
        AppId = Guid.NewGuid(),
        ModuleType = "action",
        ModuleKey = "provision-hosted-runner",
        TriggerType = "internal",
        Status = status,
        FailureSummary = failureSummary,
        CreatedAtUtc = now.AddMinutes(-10)
    };

    private static HostedRunnerLifecycleReconciler CreateReconciler(
        ApplicationDbContext db,
        RecordingPublisher publisher,
        IAuditEventService? auditEventService = null) =>
        new(db, new InlineTransactionalExecutor(), publisher, auditEventService ?? new NoOpAuditService(),
            new OptionsSnapshot(new HostedRunnerOptions
            {
                LifecycleRecoveryDelaySeconds = 60,
                LifecycleRecoveryRetrySeconds = 300
            }), NullLogger<HostedRunnerLifecycleReconciler>.Instance);

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed record MissingExecutionResult(
        int FirstPass,
        int SecondPass,
        int ProvisionEvents,
        int ModuleExecutions,
        string OperationStatus,
        string RunnerStatus,
        bool HasRecoveryAttempt);

    private sealed record TerminalExecutionResult(
        int FirstPass,
        int SecondPass,
        string OperationStatus,
        string? Error,
        string RunnerStatus,
        int FailureEvents,
        int AuditEvents);

    private sealed class InlineTransactionalExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
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
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAuditService : IAuditEventService
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OptionsSnapshot(HostedRunnerOptions value) : IOptionsSnapshot<HostedRunnerOptions>
    {
        public HostedRunnerOptions Value => value;
        public HostedRunnerOptions Get(string? name) => value;
    }
}
