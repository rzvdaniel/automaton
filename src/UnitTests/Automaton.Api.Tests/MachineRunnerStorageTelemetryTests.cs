using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class MachineRunnerStorageTelemetryTests
{
    [Test]
    public async Task ReportMachineRunner_StoresLatestWorkspaceFilesystemCapacity()
    {
        await using var db = CreateDb();
        var runner = CreateMachineRunner();
        db.Runners.Add(runner);
        await db.SaveChangesAsync();
        var authenticatedRunner = new RunnerDetails
        {
            Id = runner.Id, Name = runner.Name, Purpose = runner.Purpose,
            ApplicationProfile = runner.ApplicationProfile, InstallationId = runner.InstallationId
        };

        await new RunnerStorageTelemetryService(db).ReportMachineRunnerAsync(
            authenticatedRunner, new(DateTime.UtcNow, 1000, 400, true, null, "1.2.3"), default);

        Assert.Multiple(() =>
        {
            Assert.That(runner.MachineStorageTotalBytes, Is.EqualTo(1000));
            Assert.That(runner.MachineStorageAvailableBytes, Is.EqualTo(400));
            Assert.That(runner.MachineStorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Succeeded));
            Assert.That(runner.MachineStorageMeasuredAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public void ReportMachineRunner_RejectsNonMachineManagementIdentity()
    {
        using var db = CreateDb();
        var authenticatedRunner = new RunnerDetails
        {
            Id = Guid.NewGuid(), Name = "standard", Purpose = RunnerPurposes.Managed
        };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RunnerStorageTelemetryService(db)
            .ReportMachineRunnerAsync(authenticatedRunner, new(DateTime.UtcNow, 1000, 400, true, null, "1"), default));
    }

    [Test]
    public async Task Controller_StoresCapacityForAuthenticatedMachineRunner()
    {
        await using var db = CreateDb();
        var apiKey = CreateApiKey();
        var runner = CreateMachineRunner();
        runner.ApiKeyId = apiKey.Id;
        db.ApiKeys.Add(apiKey);
        db.Runners.Add(runner);
        await db.SaveChangesAsync();

        var result = await CreateController(db, apiKey).ReportMachineRunnerStorage(
            new(DateTime.UtcNow, 1000, 400, true, null, "1.2.3"), CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(runner.MachineStorageAvailableBytes, Is.EqualTo(400));
    }

    [Test]
    public async Task Controller_RejectsStorageReportFromStandardRunner()
    {
        await using var db = CreateDb();
        var apiKey = CreateApiKey();
        db.ApiKeys.Add(apiKey);
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(), Name = "standard", Purpose = RunnerPurposes.Managed,
            ApplicationProfile = "Standard", ApiKeyId = apiKey.Id
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db, apiKey).ReportMachineRunnerStorage(
            new(DateTime.UtcNow, 1000, 400, true, null, "1.2.3"), CancellationToken.None);

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    private static Runner CreateMachineRunner() => new()
    {
        Id = Guid.NewGuid(), Name = "machine", Purpose = RunnerPurposes.Managed,
        ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
        InstallationId = $"sh_{Guid.NewGuid():N}"
    };

    private static ApiKey CreateApiKey() => new()
    {
        Id = Guid.NewGuid(), Name = "machine-runner", HashedKey = "hash", KeyPrefix = "prefix", CreatedAt = DateTime.UtcNow
    };

    private static RunnerStorageTelemetryController CreateController(ApplicationDbContext db, ApiKey apiKey)
    {
        var controller = new RunnerStorageTelemetryController(
            new RunnerStorageTelemetrySubmissionService(db, new RunnerStorageTelemetryService(db)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("api_key_id", apiKey.Id.ToString())], "test"))
            }
        };
        return controller;
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
