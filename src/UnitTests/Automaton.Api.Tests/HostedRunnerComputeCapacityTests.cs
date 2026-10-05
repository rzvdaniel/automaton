using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerComputeCapacityTests
{
    [TestCase(true, 1, 6, 2, false)]
    [TestCase(true, 1, 6, 1, true)]
    [TestCase(false, 1, 6, 2, false)]
    [TestCase(false, 1, 6, 1, true)]
    [TestCase(true, 6, 1, 2, false)]
    [TestCase(true, 6, 1, 1, true)]
    [TestCase(false, 6, 1, 2, false)]
    [TestCase(false, 6, 1, 1, true)]
    [TestCase(true, 6, 0, 2, false)]
    [TestCase(true, 6, 0, 1, true)]
    [TestCase(false, 6, 0, 2, false)]
    [TestCase(false, 6, 0, 1, true)]
    public async Task CompetingPlacementReservesCurrentAndPendingCompute(
        bool cpu, int current, int pending, int requested, bool admitted)
    {
        await using var db = CreateDb();
        var host = CreateHost();
        host.HostedRunners.Add(CreateRunner(host, cpu, current, pending));
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var result = await SelectAsync(db, Resources(cpu, requested));

        Assert.That(result, Is.EqualTo(admitted));
    }

    [TestCase(true, 6, 0, 1, true)]
    [TestCase(true, 6, 0, 2, false)]
    [TestCase(false, 6, 0, 1, true)]
    [TestCase(false, 6, 0, 2, false)]
    [TestCase(true, 1, 6, 1, true)]
    [TestCase(true, 1, 6, 2, false)]
    [TestCase(false, 1, 6, 1, true)]
    [TestCase(false, 1, 6, 2, false)]
    public async Task ResizeDoesNotReleaseComputeBeforeConfirmationOrCountRunnerTwice(
        bool cpu, int current, int pending, int competing, bool admitted)
    {
        await using var db = CreateDb();
        var host = CreateHost();
        var resizing = CreateRunner(host, cpu, current, pending);
        host.HostedRunners.Add(resizing);
        host.HostedRunners.Add(CreateRunner(host, cpu, competing, 0));
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var result = await SelectAsync(db, Resources(cpu, 1), resizing.Id);

        Assert.That(result, Is.EqualTo(admitted));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfirmedDowngradeReleasesComputeForCompetingPlacement(bool cpu)
    {
        await using var db = CreateDb();
        var host = CreateHost();
        var runner = CreateRunner(host, cpu, 6, 1);
        host.HostedRunners.Add(runner);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();
        Assert.That(await SelectAsync(db, Resources(cpu, 2)), Is.False);

        runner.SpecificationSnapshot = runner.PendingSpecificationSnapshot!;
        runner.PendingSpecificationSnapshot = null;
        runner.Status = HostedRunnerStatuses.Running;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.That(await SelectAsync(db, Resources(cpu, 2)), Is.True);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static HostedRunner CreateRunner(RunnerHost host, bool cpu, int current, int pending) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerHostId = host.Id,
        Status = HostedRunnerStatuses.Provisioning,
        SpecificationSnapshot = new() { Resources = Resources(cpu, current) },
        PendingSpecificationSnapshot = pending == 0 ? null : new() { Resources = Resources(cpu, pending) }
    };

    private static HostedRunnerResources Resources(bool cpu, int units) => new()
    {
        CpuLimit = cpu ? units : 0.5m,
        MemoryLimitMb = cpu ? 384 : units * 1024
    };

    private static RunnerHost CreateHost() => new()
    {
        Id = Guid.NewGuid(), Name = "compute-capacity-test", Provider = RunnerHostProviders.Local,
        Status = RunnerHostStatuses.Active, HostedRunnerSchedulingEnabled = true,
        ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
        InfrastructureRunnerId = Guid.NewGuid(),
        RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
        ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
        ValidatedAtUtc = DateTime.UtcNow, ApprovedAtUtc = DateTime.UtcNow,
        TotalCpuCores = 8, ReservedCpuCores = 1, TotalMemoryMb = 8192, ReservedMemoryMb = 1024,
        TotalDiskMb = 16384, DockerFilesystemTotalBytes = 16384L * 1024 * 1024,
        StorageState = RunnerHostStorageStates.Healthy, StorageAvailableBytes = 10L * 1024 * 1024 * 1024,
        StorageReserveRequiredBytes = 0, StorageMeasuredAtUtc = DateTime.UtcNow
    };

    private static async Task<bool> SelectAsync(
        ApplicationDbContext db, HostedRunnerResources requested, Guid? resizing = null)
    {
        var placement = new HostedRunnerHostPlacementService(db,
            new HostedRunnerTestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions()));
        var selection = await placement.SelectHostAsync(Guid.NewGuid(), requested, resizing,
            1234567, null, CancellationToken.None);
        return selection.Host is not null;
    }
}
