using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
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
                new HostedRunnerStorageVerification { Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "running-1", Status = StorageVerificationStatuses.Running, StartedAtUtc = DateTime.UtcNow },
                new HostedRunnerStorageVerification { Id = Guid.NewGuid(), HostedRunnerId = runnerId, IdempotencyKey = "running-2", Status = StorageVerificationStatuses.Running, StartedAtUtc = DateTime.UtcNow },
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
            db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test" });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
                StorageAllowanceBytes = 100, StorageTotalBytes = 120, StorageState = HostedRunnerStorageStates.OverLimit,
                StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified,
                StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded, StorageMeasuredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), new NoOpPublisher(), new NoOpTransactionalEventExecutor(),
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
