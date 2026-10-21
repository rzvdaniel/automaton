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
    [TestCase(HostedRunnerDeploymentActions.Provision)]
    [TestCase(HostedRunnerDeploymentActions.Reprovision)]
    public async Task StaleRetiredProvisioningOperationFailsWithoutRedispatch(string action)
    {
        var result = await ReconcileRetiredProvisioningAsync(action);
        Assert.Multiple(() =>
        {
            Assert.That(result.Reconciled, Is.EqualTo(1));
            Assert.That(result.OperationStatus, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(result.Error, Does.Contain("Assign a Management Runner"));
            Assert.That(result.RunnerStatus, Is.EqualTo(HostedRunnerStatuses.Failed));
            Assert.That(result.PublishedMessages, Is.Zero);
            Assert.That(result.AuditEvents, Is.EqualTo(1));
        });
    }

    private static async Task<RetiredProvisioningResult> ReconcileRetiredProvisioningAsync(string action)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (runner, operation) = SeedOperation(db, action, HostedRunnerDeploymentStatuses.Executing, now);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var audit = new RecordingAuditService();

        var reconciled = await CreateReconciler(db, publisher, audit).ReconcileAsync(now, CancellationToken.None);

        var current = await db.HostedRunnerDeployments.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        var currentRunner = await db.HostedRunners.AsNoTracking().SingleAsync(x => x.Id == runner.Id);
        return new RetiredProvisioningResult(
            reconciled, current.Status, current.Error, currentRunner.Status, publisher.Messages.Count, audit.Requests.Count);
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
            Assert.That(result.AuditEvents, Is.EqualTo(1));
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
            Assert.That(result.AuditEvents, Is.EqualTo(1));
        });
    }

    private static async Task<TerminalExecutionResult> ReconcileTerminalExecutionScenarioAsync(
        string executionStatus,
        string? failureSummary = null)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var (runner, operation) = SeedOperation(
            db, HostedRunnerDeploymentActions.Restart, HostedRunnerDeploymentStatuses.Executing, now);
        db.AppModuleExecutions.Add(CreateExecution(operation.Id, now, executionStatus, failureSummary));
        await db.SaveChangesAsync();

        var audit = new RecordingAuditService();
        var reconciler = CreateReconciler(db, new RecordingPublisher(), audit);
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
        ModuleKey = "restart-hosted-runner",
        TriggerType = "internal",
        Status = status,
        FailureSummary = failureSummary,
        CreatedAtUtc = now.AddMinutes(-10)
    };

    private static HostedRunnerLifecycleReconciler CreateReconciler(
        ApplicationDbContext db,
        RecordingPublisher publisher,
        RecordingAuditService audit) =>
        new(db, new InlineTransactionalExecutor(), publisher, audit,
            new OptionsSnapshot(new HostedRunnerOptions
            {
                LifecycleRecoveryDelaySeconds = 60,
                LifecycleRecoveryRetrySeconds = 300
            }), NullLogger<HostedRunnerLifecycleReconciler>.Instance);

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed record TerminalExecutionResult(
        int FirstPass,
        int SecondPass,
        string OperationStatus,
        string? Error,
        string RunnerStatus,
        int AuditEvents);

    private sealed record RetiredProvisioningResult(
        int Reconciled,
        string OperationStatus,
        string? Error,
        string RunnerStatus,
        int PublishedMessages,
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
