using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>One-click Runner upgrade: who may start it, when it is refused, and what it reports.</summary>
[TestFixture]
public sealed class RunnerUpgradeServiceTests
{
    private const string Image = "ghcr.io/rzvdaniel/automaton-runner@sha256:9c03fb8f4b85f227ed741fd7e5e51e0fddc64a9a700725900ce8ca4ffd226338";
    private const string Old = "ghcr.io/rzvdaniel/automaton-runner@sha256:1111111111111111111111111111111111111111111111111111111111111111";

    // Synchronous on purpose: the signed-in user lives in an AsyncLocal that an async helper would lose.
    private static (RunnerUpgradeService Service, FakeRunner Fake, Guid Runner, Automaton.Api.Data.ApplicationDbContext Db) SetUp(
        bool owner = true, bool online = true, string? digest = Old)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid runner = Guid.NewGuid(), ownerId = Guid.NewGuid();
        db.Runners.Add(new Runner
        {
            Id = runner, Name = "Machine", Purpose = Automaton.Core.Enums.RunnerPurposes.Managed, InstallationId = $"sh_{Guid.NewGuid():N}",
            ConnectionId = online ? "c" : null, LastHeartbeatAtUtc = DateTime.UtcNow, ImageDigest = digest
        });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner, UserId = ownerId });
        db.SaveChanges();
        var acting = owner ? ownerId : Guid.NewGuid();
        var users = AppAuthorizationTestData.CreateUserContextService(acting);
        var fake = new FakeRunner();
        return (new RunnerUpgradeService(db, new ResourceAuthorizationService(db, users), fake,
            Options.Create(new HostedRunnerOptions { DockerImage = Image })), fake, runner, db);
    }

    [Test]
    public async Task TheOwnerStartsAnUpgradeThatUsesOnlyTheConfiguredImage()
    {
        var (service, fake, runner, _) = SetUp();
        var status = await service.StartAsync(runner, default);
        Assert.That(status.State, Is.EqualTo(RunnerUpgradeStates.InProgress));
        Assert.That(fake.Kind, Is.EqualTo("upgrade-runner"));
        var request = (UpgradeRunnerRequest)fake.Payload!;
        Assert.That(request.RunnerId, Is.EqualTo(runner));
        Assert.That(request.Script, Does.Contain($"image='{Image}'"));
    }

    [Test]
    public async Task AStrangerCannotStartOrReadAnUpgrade()
    {
        var (service, fake, runner, _) = SetUp(owner: false);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StartAsync(runner, default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StatusAsync(runner, default));
        Assert.That(fake.Kind, Is.Null);
    }

    [Test]
    public async Task AnOfflineRunnerIsRefusedWithTheManualRoute()
    {
        var (service, fake, runner, _) = SetUp(online: false);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(runner, default));
        Assert.That(error!.Message, Does.Contain("manual upgrade command"));
        Assert.That(fake.Kind, Is.Null);
    }

    [Test]
    public async Task ARunnerOnTheCurrentImageIsNotUpgraded()
    {
        var (service, fake, runner, _) = SetUp(digest: Image);
        Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(runner, default));
        Assert.That(fake.Kind, Is.Null);
    }

    [Test]
    public async Task ARunnerWithARunningDeployOrActionIsRefused()
    {
        var (service, fake, runner, db) = SetUp();
        db.RunnerExecutionOperations.Add(new RunnerExecutionOperation
            { Id = Guid.NewGuid(), RunnerId = runner, RequestedByUserId = Guid.NewGuid(), ProtectedRequest = "x" });
        await db.SaveChangesAsync();
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(runner, default));
        Assert.That(error!.Message, Does.Contain("deploy or an Action"));
        Assert.That(fake.Kind, Is.Null);
    }

    [Test]
    public async Task AnOldRunnerThatDoesNotKnowTheCommandGetsTheManualHint()
    {
        var (service, fake, runner, _) = SetUp();
        fake.Error = new InvalidOperationException("Unknown App Action command.");
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(runner, default));
        Assert.That(error!.Message, Does.Contain("too old to upgrade itself"));
    }

    [TestCase("running", RunnerUpgradeStates.InProgress)]
    [TestCase("failed", RunnerUpgradeStates.RolledBack)]
    [TestCase("succeeded", RunnerUpgradeStates.Reconnected)]
    [TestCase("none", RunnerUpgradeStates.Unknown)]
    public async Task StatusFollowsTheHelperWhileTheRunnerStillHasTheOldImage(string helper, string expected)
    {
        var (service, fake, runner, _) = SetUp();
        fake.Response = new RunnerUpgradeHelperStatus(helper, "log line");
        Assert.That((await service.StatusAsync(runner, default)).State, Is.EqualTo(expected));
    }

    [Test]
    public async Task ARunnerBackOnTheCurrentImageHasReconnected()
    {
        var (service, fake, runner, _) = SetUp(digest: Image);
        Assert.That((await service.StatusAsync(runner, default)).State, Is.EqualTo(RunnerUpgradeStates.Reconnected));
        Assert.That(fake.Kind, Is.Null);
    }

    [Test]
    public async Task AnOfflineRunnerIsStillInProgress()
    {
        var (service, _, runner, _) = SetUp(online: false);
        Assert.That((await service.StatusAsync(runner, default)).State, Is.EqualTo(RunnerUpgradeStates.InProgress));
    }
}

internal sealed class FakeRunner : IRunnerActionCommands
{
    public string? Kind { get; private set; }
    public object? Payload { get; private set; }
    public Exception? Error { get; set; }
    public object Response { get; set; } = new UpgradeRunnerStarted(true);

    public Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
    {
        Kind = kind;
        Payload = payload;
        if (Error is not null) throw Error;
        return Task.FromResult((T)(kind == "upgrade-runner" ? new UpgradeRunnerStarted(true) : Response));
    }
}
