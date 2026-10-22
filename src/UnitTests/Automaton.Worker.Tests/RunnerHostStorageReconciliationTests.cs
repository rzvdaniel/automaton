using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class RunnerHostStorageReconciliationTests
{
    private ServiceProvider provider = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        provider = services.BuildServiceProvider();
        var oldMeasurement = DateTime.UtcNow.AddHours(-3);
        var hostedId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.RunnerHosts.AddRange(
                new RunnerHost { Id = Guid.NewGuid(), Name = "stale", Provider = "test", StorageMeasuredAtUtc = oldMeasurement },
                new RunnerHost { Id = Guid.NewGuid(), Name = "fresh", Provider = "test", StorageMeasuredAtUtc = DateTime.UtcNow, StorageState = RunnerHostStorageStates.Healthy });
            db.HostedRunners.Add(new HostedRunner
            {
                Id = hostedId, UserId = Guid.NewGuid(), Name = "historical", ContainerName = "historical",
                StorageMeasuredAtUtc = oldMeasurement, StorageState = HostedRunnerStorageStates.Healthy
            });
            db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
            {
                Id = Guid.NewGuid(), HostedRunnerId = hostedId, IdempotencyKey = "historical",
                Status = StorageVerificationStatuses.Pending, RequestedAtUtc = oldMeasurement
            });
            await db.SaveChangesAsync();
        }

    }

    [TearDown]
    public async Task TearDownAsync() => await provider.DisposeAsync();

    [Test]
    public async Task Reconcile_ExpiresHostTelemetryWithoutChangingHistoricalHostedStorage()
    {
        var reconciliation = new RunnerHostStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new StorageTelemetryOptions()));
        await reconciliation.ReconcileAsync(CancellationToken.None);
        await reconciliation.ReconcileAsync(CancellationToken.None);

        await AssertPersistedStorageAsync();
    }

    private async Task AssertPersistedStorageAsync()
    {
        await using var checkScope = provider.CreateAsyncScope();
        var persisted = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That((await persisted.RunnerHosts.SingleAsync(host => host.Name == "stale")).StorageState,
            Is.EqualTo(RunnerHostStorageStates.Stale));
        Assert.That((await persisted.RunnerHosts.SingleAsync(host => host.Name == "fresh")).StorageState,
            Is.EqualTo(RunnerHostStorageStates.Healthy));
        Assert.That((await persisted.HostedRunners.SingleAsync()).StorageState, Is.EqualTo(HostedRunnerStorageStates.Healthy));
        Assert.That((await persisted.HostedRunnerStorageVerifications.SingleAsync()).Status, Is.EqualTo(StorageVerificationStatuses.Pending));
        Assert.That(await persisted.HostedRunnerStorageObservations.CountAsync(), Is.Zero);
        Assert.That(await persisted.AccountStorageAllocations.CountAsync(), Is.Zero);
    }
}
