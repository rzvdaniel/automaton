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
            "/var/lib/docker", 20L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024, true, null, "1", true), default);

        Assert.Multiple(() =>
        {
            Assert.That(host.StorageTotalBytes, Is.EqualTo(20L * 1024 * 1024 * 1024));
            Assert.That(host.StorageAvailableBytes, Is.EqualTo(2L * 1024 * 1024 * 1024));
            Assert.That(host.RootFilesystemAvailableBytes, Is.EqualTo(150L * 1024 * 1024 * 1024));
            Assert.That(host.StorageState, Is.EqualTo(RunnerHostStorageStates.SchedulingBlocked).Or.EqualTo(RunnerHostStorageStates.Critical));
        });
    }

    [Test]
    public async Task ReportRunnerHost_DoesNotAdmitTelemetryWithoutBoundedImageStoreProof()
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
            "/var/lib/docker", 20L * 1024 * 1024 * 1024, 15L * 1024 * 1024 * 1024, true, null, "legacy-runner"), default);

        Assert.Multiple(() =>
        {
            Assert.That(host.StorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Failed));
            Assert.That(host.StorageState, Is.EqualTo(RunnerHostStorageStates.MeasurementFailed));
            Assert.That(host.StorageMeasurementError, Does.Contain("did not verify Docker image/cache storage"));
        });
    }

    [Test]
    public async Task SubmissionService_ResolvesInfrastructureHostFromApiKey()
    {
        await using var db = CreateDb();
        var apiKey = CreateApiKey();
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-runner",
            Purpose = RunnerPurposes.Infrastructure,
            ApiKeyId = apiKey.Id
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host",
            Provider = "test",
            InfrastructureRunnerId = runner.Id,
            StorageState = RunnerHostStorageStates.Unknown
        };
        db.AddRange(apiKey, runner, host);
        await db.SaveChangesAsync();
        var submission = CreateSubmissionService(db);

        await submission.ReportRunnerHostAsync(apiKey.Id, new(
            host.Id, DateTime.UtcNow, "/", 1000, 500, 500, "/var/lib/docker", 800, 400,
            true, null, "1", true), CancellationToken.None);

        Assert.That(host.StorageMeasuredAtUtc, Is.Not.Null);
        Assert.That(host.StorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Succeeded));
    }

    [Test]
    public void SubmissionService_RejectsWrongRunnerPurpose()
    {
        using var db = CreateDb();
        var apiKey = CreateApiKey();
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-runner",
            Purpose = RunnerPurposes.Managed,
            ApiKeyId = apiKey.Id
        });
        db.ApiKeys.Add(apiKey);
        db.SaveChanges();
        var submission = CreateSubmissionService(db);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => submission.ReportRunnerHostAsync(
            apiKey.Id, new(Guid.NewGuid(), DateTime.UtcNow, "/", 10, 5, 5, "/docker", 10, 5,
                true, null, "1", true), CancellationToken.None));
    }

    private static RunnerStorageTelemetrySubmissionService CreateSubmissionService(ApplicationDbContext db)
        => new(db, new RunnerStorageTelemetryService(db));

    private static ApiKey CreateApiKey() => new()
    {
        Id = Guid.NewGuid(), Name = "runner", HashedKey = "hash", KeyPrefix = "prefix", CreatedAt = DateTime.UtcNow
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

}
