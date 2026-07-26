using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public class RunnerStorageTelemetryServiceTests
{
    [Test]
    public async Task ReportHostedRunner_AcceptsMatchingIdentityAndRecalculatesTotals()
    {
        await using var db = CreateDb();
        var runner = SeedHostedRunner(db);
        var service = new RunnerStorageTelemetryService(db);

        await service.ReportHostedRunnerAsync(Authenticated(runner), new(
            runner.Id, DateTime.UtcNow, 10, 20, 30, 40, 999, 999, 12, true, null, "1.2.3"), default);

        Assert.Multiple(() =>
        {
            Assert.That(runner.StorageUserBytes, Is.EqualTo(90));
            Assert.That(runner.StorageTotalBytes, Is.EqualTo(100));
            Assert.That(runner.StorageState, Is.EqualTo(HostedRunnerStorageStates.Healthy));
            Assert.That(db.HostedRunnerStorageObservations.Single().TotalBytes, Is.EqualTo(100));
        });
    }

    [Test]
    public void ReportHostedRunner_RejectsAnotherHostedRunnerIdentity()
    {
        using var db = CreateDb();
        var runner = SeedHostedRunner(db);
        var authenticated = Authenticated(runner);
        authenticated.HostedRunnerId = Guid.NewGuid();
        var service = new RunnerStorageTelemetryService(db);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReportHostedRunnerAsync(authenticated, new(
            runner.Id, DateTime.UtcNow, 1, 1, 1, 1, 3, 4, 1, true, null, "1"), default));
    }

    [Test]
    public async Task ReportHostedRunner_OlderObservationDoesNotReplaceCurrent()
    {
        await using var db = CreateDb();
        var runner = SeedHostedRunner(db);
        runner.StorageMeasuredAtUtc = DateTime.UtcNow;
        runner.StorageTotalBytes = 500;
        await db.SaveChangesAsync();

        await new RunnerStorageTelemetryService(db).ReportHostedRunnerAsync(Authenticated(runner), new(
            runner.Id, runner.StorageMeasuredAtUtc.Value.AddMinutes(-1), 1, 1, 1, 1, 3, 4, 1, true, null, "1"), default);

        Assert.That(runner.StorageTotalBytes, Is.EqualTo(500));
        Assert.That(db.HostedRunnerStorageObservations, Is.Empty);
    }

    [Test]
    public async Task ReportHostedRunner_FailurePersistsWithoutReplacingUsageWithZero()
    {
        await using var db = CreateDb();
        var runner = SeedHostedRunner(db);
        runner.StorageTotalBytes = 500;
        await db.SaveChangesAsync();

        await new RunnerStorageTelemetryService(db).ReportHostedRunnerAsync(Authenticated(runner), new(
            runner.Id, DateTime.UtcNow, 0, 0, 0, 0, 0, 0, 1, false, "denied", "1"), default);

        Assert.Multiple(() =>
        {
            Assert.That(runner.StorageTotalBytes, Is.EqualTo(500));
            Assert.That(runner.StorageState, Is.EqualTo(HostedRunnerStorageStates.MeasurementFailed));
            Assert.That(db.HostedRunnerStorageObservations.Single().Success, Is.False);
        });
    }

    [Test]
    public async Task RecentAuthoritativeObservation_TakesPrecedenceOverFailedSelfReport()
    {
        await using var db = CreateDb();
        var runner = SeedHostedRunner(db);
        runner.StorageVerifiedAtUtc = DateTime.UtcNow;
        runner.StorageVerifiedTotalBytes = 600;
        runner.StorageTotalBytes = 600;
        runner.StorageState = HostedRunnerStorageStates.Warning;
        runner.StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified;
        await db.SaveChangesAsync();

        await new RunnerStorageTelemetryService(db).ReportHostedRunnerAsync(Authenticated(runner), new(
            runner.Id, DateTime.UtcNow.AddSeconds(1), 0, 0, 0, 0, 0, 0, 1, false, "denied", "1"), default);

        Assert.Multiple(() =>
        {
            Assert.That(runner.StorageTotalBytes, Is.EqualTo(600));
            Assert.That(runner.StorageState, Is.EqualTo(HostedRunnerStorageStates.Warning));
            Assert.That(runner.StorageMeasurementSource, Is.EqualTo(StorageMeasurementSources.InfrastructureRunnerVerified));
            Assert.That(runner.StorageSelfReportedStatus, Is.EqualTo(StorageMeasurementStatuses.Failed));
        });
    }

    [Test]
    public async Task ReportRunnerHost_UsesDockerFilesystemForCapacityState()
    {
        await using var db = CreateDb();
        var infrastructureRunner = new Runner { Id = Guid.NewGuid(), Name = "infrastructure", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = infrastructureRunner.Id,
            StorageState = RunnerHostStorageStates.Unknown
        };
        db.Runners.Add(infrastructureRunner);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        await new RunnerStorageTelemetryService(db).ReportRunnerHostAsync(new RunnerDetails
        {
            Id = infrastructureRunner.Id, Name = infrastructureRunner.Name, Purpose = RunnerPurposes.Infrastructure,
            RunnerHostId = host.Id
        }, new(
            host.Id, DateTime.UtcNow, "/", 200L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024, 150L * 1024 * 1024 * 1024,
            "/var/lib/docker", 20L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024, true, null, "1"), default);

        Assert.Multiple(() =>
        {
            Assert.That(host.StorageTotalBytes, Is.EqualTo(20L * 1024 * 1024 * 1024));
            Assert.That(host.StorageAvailableBytes, Is.EqualTo(2L * 1024 * 1024 * 1024));
            Assert.That(host.RootFilesystemAvailableBytes, Is.EqualTo(150L * 1024 * 1024 * 1024));
            Assert.That(host.StorageState, Is.EqualTo(RunnerHostStorageStates.SchedulingBlocked).Or.EqualTo(RunnerHostStorageStates.Critical));
        });
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static HostedRunner SeedHostedRunner(ApplicationDbContext db)
    {
        var runnerId = Guid.NewGuid();
        var hosted = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerId = runnerId,
            Name = "hosted", ContainerName = "hosted", StorageAllowanceBytes = 1000
        };
        db.HostedRunners.Add(hosted);
        db.SaveChanges();
        return hosted;
    }

    private static RunnerDetails Authenticated(HostedRunner runner) => new()
    {
        Id = runner.RunnerId!.Value, Name = runner.Name, Purpose = RunnerPurposes.Managed,
        HostedRunnerId = runner.Id
    };
}
