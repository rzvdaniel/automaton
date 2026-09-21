using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public class HostedRunnerStorageReconciliationServiceTests
{
    [Test]
    public async Task RecentHealthyReport_DoesNotRequestVerification()
    {
        await using var fixture = CreateFixture(HostedRunnerStorageStates.Healthy, DateTime.UtcNow);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task RecentHealthySelfReportWithoutPriorVerification_DoesNotRequestVerification()
    {
        await using var fixture = CreateFixture(HostedRunnerStorageStates.Healthy, DateTime.UtcNow);
        var runner = fixture.Db.HostedRunners.Single();
        runner.StorageVerifiedAtUtc = null;
        runner.StorageSelfReportedStatus = StorageMeasurementStatuses.Succeeded;
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task ExpiredHealthyVerification_IsSampledWhenAuditModeDoesNotSelectRunner()
    {
        await using var fixture = CreateFixture(HostedRunnerStorageStates.Healthy, DateTime.UtcNow,
            new StorageTelemetryOptions { EnableLowFrequencyVerification = true, LowFrequencyVerificationSamplePercent = 0 });
        fixture.Db.HostedRunners.Single().StorageVerifiedAtUtc = DateTime.UtcNow.AddDays(-2);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task StaleReport_RequestsExactlyOneVerificationAcrossRepeatedReconciliation()
    {
        await using var fixture = CreateFixture(HostedRunnerStorageStates.Healthy, DateTime.UtcNow.AddHours(-3));
        await fixture.Service.ReconcileAsync(default);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Count(), Is.EqualTo(1));
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Single().Reason, Is.EqualTo("Stale"));
    }

    [Test]
    public async Task WarningReport_RequestsVerification()
    {
        await using var fixture = CreateFixture(HostedRunnerStorageStates.Warning, DateTime.UtcNow);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Single().Reason, Is.EqualTo("Threshold"));
    }

    [Test]
    public async Task DispatchBound_ExcludesAlreadyRunningVerificationsPerHost()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test" });
            var runner = new HostedRunner
            {
                Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageMeasuredAtUtc = DateTime.UtcNow, StorageSelfReportedStatus = StorageMeasurementStatuses.Succeeded,
                StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded, StorageState = HostedRunnerStorageStates.Healthy
            };
            db.HostedRunners.Add(runner);
            db.HostedRunnerStorageVerifications.AddRange(
                new HostedRunnerStorageVerification { Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "dispatching", Status = StorageVerificationStatuses.Dispatching, StartedAtUtc = DateTime.UtcNow },
                new HostedRunnerStorageVerification { Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "running", Status = StorageVerificationStatuses.Running, StartedAtUtc = DateTime.UtcNow },
                new HostedRunnerStorageVerification { Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "pending", Status = StorageVerificationStatuses.Pending, RequestedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), new NoOpPublisher(), new NoOpTransactionalEventExecutor(), Options.Create(new StorageTelemetryOptions()));

        await service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That((await verifyDb.HostedRunnerStorageVerifications.SingleAsync(x => x.IdempotencyKey == "pending")).Status,
            Is.EqualTo(StorageVerificationStatuses.Pending));
    }

    [Test]
    public async Task DispatchBound_IgnoresStalePendingDeploymentMirrors()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        var infrastructureRunnerId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var verificationId = Guid.NewGuid();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost
            {
                Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = infrastructureRunnerId
            });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageProjectId = 1_234_568,
                StorageMeasuredAtUtc = DateTime.UtcNow, StorageSelfReportedStatus = StorageMeasurementStatuses.Succeeded,
                StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded, StorageState = HostedRunnerStorageStates.Healthy
            });
            db.HostedRunnerDeployments.AddRange(
                new HostedRunnerDeployment
                {
                    Id = Guid.NewGuid(), HostedRunnerId = runnerId, Action = HostedRunnerDeploymentActions.VerifyStorage,
                    Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = DateTime.UtcNow.AddDays(-50)
                },
                new HostedRunnerDeployment
                {
                    Id = Guid.NewGuid(), HostedRunnerId = runnerId, Action = HostedRunnerDeploymentActions.VerifyStorage,
                    Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = DateTime.UtcNow.AddDays(-49)
                });
            db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
            {
                Id = verificationId, HostedRunnerId = runnerId, IdempotencyKey = "fresh-verification",
                Status = StorageVerificationStatuses.Pending, RequestedAtUtc = DateTime.UtcNow, Reason = "AdministratorRequested"
            });
            await db.SaveChangesAsync();
        }

        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher, new NoOpTransactionalEventExecutor(),
            Options.Create(new StorageTelemetryOptions { MaxVerificationDispatchesPerHost = 2 }));

        await service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verification = await verifyDb.HostedRunnerStorageVerifications.SingleAsync(x => x.Id == verificationId);
        var command = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(verifyDb.HostedRunnerDeployments.Count(x => x.Action == HostedRunnerDeploymentActions.VerifyStorage), Is.EqualTo(3));
            Assert.That(command.CommandId, Is.EqualTo(verificationId));
            Assert.That(command.OperationId, Is.EqualTo(verificationId));
            Assert.That(command.ActionKey, Is.EqualTo(StorageTelemetryActions.MeasureHostedRunnerStorage));
            Assert.That(command.RunnerHostId, Is.EqualTo(hostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(infrastructureRunnerId));
        });
    }

    [Test]
    public async Task AdministratorRequestedVerification_OutranksRoutineBacklog()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var administratorVerificationId = Guid.NewGuid();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid() });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageProjectId = 1_234_567,
                StorageState = HostedRunnerStorageStates.Healthy, StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
                StorageMeasuredAtUtc = DateTime.UtcNow
            });
            db.HostedRunnerStorageVerifications.AddRange(
                new HostedRunnerStorageVerification
                {
                    Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "old-stale", Status = StorageVerificationStatuses.Pending,
                    Reason = "Stale", RequestedAtUtc = DateTime.UtcNow.AddDays(-2)
                },
                new HostedRunnerStorageVerification
                {
                    Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "old-never-measured", Status = StorageVerificationStatuses.Pending,
                    Reason = "NeverMeasured", RequestedAtUtc = DateTime.UtcNow.AddDays(-1)
                },
                new HostedRunnerStorageVerification
                {
                    Id = administratorVerificationId, HostedRunnerId = runnerId, IdempotencyKey = "administrator", Status = StorageVerificationStatuses.Pending,
                    Reason = "AdministratorRequested", RequestedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher, new NoOpTransactionalEventExecutor(),
            Options.Create(new StorageTelemetryOptions { MaxVerificationDispatchesPerHost = 1 }));

        await service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Multiple(() =>
        {
            Assert.That(verifyDb.HostedRunnerStorageVerifications.Single(x => x.Id == administratorVerificationId).Status,
                Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(verifyDb.HostedRunnerStorageVerifications.Count(x => x.Status == StorageVerificationStatuses.Pending), Is.EqualTo(2));
            Assert.That(publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single().CommandId, Is.EqualTo(administratorVerificationId));
        });
    }

    [Test]
    public void AdministratorRequestedVerification_OnStaleRunnerRetainsInteractivePriority()
    {
        var verification = new HostedRunnerStorageVerification
        {
            Reason = "AdministratorRequested",
            HostedRunner = new HostedRunner
            {
                StorageState = HostedRunnerStorageStates.Stale,
                StorageMeasurementStatus = StorageMeasurementStatuses.Stale
            }
        };

        Assert.That(HostedRunnerStorageReconciliationService.VerificationPriority(verification), Is.EqualTo(4));
    }

    [Test]
    public void CriticalStorageStateRemainsAheadOfAdministratorRequest()
    {
        var verification = new HostedRunnerStorageVerification
        {
            Reason = "AdministratorRequested",
            HostedRunner = new HostedRunner { StorageState = HostedRunnerStorageStates.OverLimit }
        };

        Assert.That(HostedRunnerStorageReconciliationService.VerificationPriority(verification), Is.LessThan(4));
    }

    [Test]
    public async Task EqualPriorityAdministratorRequestsRemainFifo()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow.AddMinutes(-2);
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid() });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageProjectId = 1_234_569,
                StorageState = HostedRunnerStorageStates.Healthy, StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
                StorageMeasuredAtUtc = DateTime.UtcNow
            });
            db.HostedRunnerStorageVerifications.AddRange(
                new HostedRunnerStorageVerification
                {
                    Id = olderId, HostedRunnerId = runnerId, IdempotencyKey = "older-administrator", Status = StorageVerificationStatuses.Pending,
                    Reason = "AdministratorRequested", RequestedAtUtc = requestedAt
                },
                new HostedRunnerStorageVerification
                {
                    Id = newerId, HostedRunnerId = runnerId, IdempotencyKey = "newer-administrator", Status = StorageVerificationStatuses.Pending,
                    Reason = "AdministratorRequested", RequestedAtUtc = requestedAt.AddMinutes(1)
                });
            await db.SaveChangesAsync();
        }

        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher, new NoOpTransactionalEventExecutor(),
            Options.Create(new StorageTelemetryOptions { MaxVerificationDispatchesPerHost = 1 }));

        await service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Multiple(() =>
        {
            Assert.That(verifyDb.HostedRunnerStorageVerifications.Single(x => x.Id == olderId).Status,
                Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(verifyDb.HostedRunnerStorageVerifications.Single(x => x.Id == newerId).Status,
                Is.EqualTo(StorageVerificationStatuses.Pending));
            Assert.That(publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single().CommandId, Is.EqualTo(olderId));
        });
    }

    [Test]
    public async Task AuthoritativeOverLimit_StartsGraceThenQueuesOneSuspension()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid() });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageAllowanceBytes = 100, StorageTotalBytes = 120, StorageState = HostedRunnerStorageStates.OverLimit,
                StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified,
                StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded, StorageMeasuredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher, new NoOpTransactionalEventExecutor(),
            Options.Create(new StorageTelemetryOptions { OverLimitGracePeriodHours = 1 }));
        await service.EnforceAsync(default);
        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var runner = await db.HostedRunners.SingleAsync();
            Assert.That(runner.EnforcementState, Is.EqualTo(HostedRunnerEnforcementStates.GracePeriod));
            runner.GracePeriodEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await service.EnforceAsync(default);
        await using var finalScope = provider.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Multiple(() =>
        {
            Assert.That(finalDb.HostedRunners.Single().EnforcementState, Is.EqualTo(HostedRunnerEnforcementStates.SuspensionPending));
            Assert.That(finalDb.HostedRunnerDeployments.Count(x => x.Action == HostedRunnerDeploymentActions.Suspend), Is.EqualTo(1));
            var command = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
            Assert.That(command.ActionKey, Is.EqualTo(StorageEnforcementActions.SuspendHostedRunner));
            Assert.That(command.RunnerHostId, Is.EqualTo(hostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(finalDb.RunnerHosts.Single().InfrastructureRunnerId));
        });
    }

    [Test]
    public async Task PendingVerification_PublishesDurableMeasurementCommand()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var hostId = Guid.NewGuid();
        var infrastructureRunnerId = Guid.NewGuid();
        var hostedRunnerId = Guid.NewGuid();
        var verificationId = Guid.NewGuid();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = infrastructureRunnerId });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = hostedRunnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner",
                RunnerHostId = hostId, StorageProjectId = 1_234_570, StorageState = HostedRunnerStorageStates.Healthy,
                StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
                StorageMeasuredAtUtc = DateTime.UtcNow.AddHours(-3)
            });
            db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
            {
                Id = verificationId, HostedRunnerId = hostedRunnerId, IdempotencyKey = "verification",
                Status = StorageVerificationStatuses.Pending, RequestedAtUtc = DateTime.UtcNow.AddMinutes(-1), Reason = "Stale"
            });
            await db.SaveChangesAsync();
        }

        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher, new NoOpTransactionalEventExecutor(),
            Options.Create(new StorageTelemetryOptions()));

        await service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

        var command = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single(x => x.CommandId == verificationId);
        Assert.Multiple(() =>
        {
            Assert.That(command.CommandId, Is.EqualTo(verificationId));
            Assert.That(command.OperationId, Is.EqualTo(verificationId));
            Assert.That(command.ActionKey, Is.EqualTo(StorageTelemetryActions.MeasureHostedRunnerStorage));
            Assert.That(command.RunnerHostId, Is.EqualTo(hostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(infrastructureRunnerId));
        });
    }

    private static Fixture CreateFixture(string state, DateTime measuredAt, StorageTelemetryOptions? options = null)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner",
            StorageState = state, StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
            StorageMeasuredAtUtc = measuredAt, StorageVerifiedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), new NoOpPublisher(), new NoOpTransactionalEventExecutor(),
            Options.Create(options ?? new StorageTelemetryOptions()));
        return new Fixture(provider, scope, db, service);
    }

    private sealed class NoOpPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default) where TEvent : class =>
            Task.CompletedTask;
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];
        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default) where TEvent : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpTransactionalEventExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class Fixture(
        ServiceProvider provider,
        IServiceScope scope,
        ApplicationDbContext db,
        HostedRunnerStorageReconciliationService service) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public HostedRunnerStorageReconciliationService Service { get; } = service;
        public ValueTask DisposeAsync()
        {
            scope.Dispose();
            return provider.DisposeAsync();
        }
    }
}
