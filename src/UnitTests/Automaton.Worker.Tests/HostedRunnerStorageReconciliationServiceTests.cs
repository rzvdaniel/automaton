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
            provider.GetRequiredService<IServiceScopeFactory>(), new NoOpPublisher(),
            Options.Create(options ?? new StorageTelemetryOptions()),
            NullLogger<HostedRunnerStorageReconciliationService>.Instance);
        return new Fixture(provider, scope, db, service);
    }

    private sealed class NoOpPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default) where TEvent : class =>
            Task.CompletedTask;
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
