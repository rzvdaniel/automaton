using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerFileTransferTargetAmbiguityTests
{
    [Test]
    public async Task MediaTransferWithoutTargetId_AllowsMultipleTargetsOnOneRunner()
    {
        var result = await RunnerFileTransferTargetAmbiguityScenario.RunAsync();

        Assert.That(result, Is.True,
            "The media command should pass preflight and remain queued for Runner delivery.");
    }
}

internal static class RunnerFileTransferTargetAmbiguityScenario
{
    public static async Task<bool> RunAsync()
    {
        await using var fixture = await CreateFixtureAsync();
        await QueueUntargetedMediaTransferAsync(fixture);
        return await fixture.Db.RunnerExecutionOperations.AnyAsync(
            item => item.Id == fixture.OperationId && item.CompletedAtUtc == null);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var runnerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = runnerId,
            ApiKeyId = Guid.NewGuid(),
            Name = "shared-runner",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = "Standard"
        };
        var live = AppDeploymentTarget.CreateLive(appId, runnerId);
        var preview = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Preview",
            Slug = "preview",
            ExecutionRunnerId = runnerId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Runners.Add(runner);
        db.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "shared app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            DeploymentTargets = { live, preview }
        });
        db.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = userId });
        await db.SaveChangesAsync();
        return new Fixture(db, runnerId, userId, appId, Guid.NewGuid());
    }

    private static async Task QueueUntargetedMediaTransferAsync(Fixture fixture)
    {
        Assert.CatchAsync<OperationCanceledException>(() => CreateGateway(fixture).ExecuteAsync(
            fixture.OperationId,
            fixture.RunnerId,
            fixture.UserId,
            CreateUntargetedMediaCommand(fixture.AppId),
            input: null,
            CancellationToken.None,
            timeout: TimeSpan.FromMilliseconds(50)));
    }

    private static RunnerFileTransferGateway CreateGateway(Fixture fixture)
    {
        var store = new DeploymentPackageStore(
            new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() },
            Options.Create(new DeploymentPackageStorageOptions()));
        return new RunnerFileTransferGateway(
            fixture.Db,
            store,
            RunnerExecutionTestServices.Operations(fixture.Db),
            new ResourceAuthorizationService(fixture.Db, null!));
    }

    private static RunnerFileTransferCommand CreateUntargetedMediaCommand(Guid appId)
        => new(RunnerFileTransferKinds.ReadMediaStats, appId, "media-stats");

    private sealed record Fixture(
        ApplicationDbContext Db,
        Guid RunnerId,
        Guid UserId,
        Guid AppId,
        Guid OperationId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
