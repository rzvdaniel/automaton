using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerWritableLayerCapacityTests
{
    [TestCase(1053, false)]
    [TestCase(1054, true)]
    public async Task SelectHostAsync_ReservesQuotaWritableLayerAndBoundedLogs(
        int hostDiskMb,
        bool hasCapacity)
    {
        await using var db = CreateDbContext();
        var host = CreateHost(hostDiskMb);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var placement = new HostedRunnerHostPlacementService(
            db,
            new HostedRunnerTestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions()));
        var result = await placement.SelectHostAsync(
            Guid.NewGuid(),
            new HostedRunnerResources(),
            excludedHostedRunnerId: null,
            accountStorageProjectId: 1234567,
            requiredStorageHostId: null,
            CancellationToken.None);

        Assert.That(result.Host is not null, Is.EqualTo(hasCapacity));
    }

    [Test]
    public async Task SelectHostAsync_ReservesPendingWritableLayerAllocation()
    {
        await using var db = CreateDbContext();
        var host = CreateHost(1800);
        host.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            RunnerHostId = host.Id,
            Status = HostedRunnerStatuses.Running,
            SpecificationSnapshot = CreateSnapshot(64),
            PendingSpecificationSnapshot = CreateSnapshot(512)
        });
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var placement = new HostedRunnerHostPlacementService(
            db,
            new HostedRunnerTestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions()));
        var result = await placement.SelectHostAsync(
            Guid.NewGuid(),
            new HostedRunnerResources(),
            excludedHostedRunnerId: null,
            accountStorageProjectId: 1234567,
            requiredStorageHostId: null,
            CancellationToken.None);

        Assert.That(result.Host, Is.Null);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RunnerHost CreateHost(int totalDiskMb) => new()
    {
        Id = Guid.NewGuid(),
        Name = "capacity-test",
        Provider = RunnerHostProviders.Local,
        Status = RunnerHostStatuses.Active,
        ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
        InfrastructureRunnerId = Guid.NewGuid(),
        RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
        ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
        ValidatedAtUtc = DateTime.UtcNow,
        ApprovedAtUtc = DateTime.UtcNow,
        TotalCpuCores = 8,
        TotalMemoryMb = 16384,
        TotalDiskMb = totalDiskMb,
        CreatedAt = DateTime.UtcNow
    };

    private static HostedRunnerSpecificationSnapshot CreateSnapshot(int writableLayerLimitMb) => new()
    {
        Resources = new HostedRunnerResources
        {
            WritableLayerLimitMb = writableLayerLimitMb
        }
    };
}
