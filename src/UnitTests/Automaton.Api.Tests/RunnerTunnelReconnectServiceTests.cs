using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerTunnelReconnectServiceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CutoverAndCompensation_SkipRetainedRevokedRunnerWithRemovedQueues(bool reconcile)
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync();
        var revokedId = await fixture.AddInfrastructureRunnerAsync(isActive: false);
        var eligibleId = await fixture.AddInfrastructureRunnerAsync(isActive: true);
        fixture.Publisher.RemovedQueueRunnerIds.Add(revokedId);

        var result = await fixture.Service.RequestForApiColorAsync("blue", reconcile, CancellationToken.None);

        Assert.That(result.FailedCount, Is.Zero);
        Assert.That(result.CandidateRunnerCount, Is.EqualTo(6));
        Assert.That(result.RequestedCount, Is.EqualTo(5));
        Assert.That(fixture.Publisher.RunnerIds, Does.Not.Contain(revokedId));
        Assert.That(fixture.Publisher.RunnerIds, Does.Contain(eligibleId));
        Assert.That(fixture.Publisher.RunnerIds, Does.Contain(fixture.DisconnectedRunnerId));
    }

    [Test]
    public async Task RequestForApiColor_QueuesLatestTargetForAllRegisteredCompatibleRunners()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync();
        var result = await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);

        Assert.That(result.CandidateRunnerCount, Is.EqualTo(5));
        Assert.That(result.RequestedCount, Is.EqualTo(4));
        Assert.That(result.FailedCount, Is.Zero);
        Assert.That(fixture.Publisher.RunnerIds, Is.EquivalentTo(new[] { fixture.PreviousRunnerId,
            fixture.CurrentRunnerId, fixture.CurrentReplicaRunnerId, fixture.DisconnectedRunnerId }));
        Assert.That(fixture.Publisher.TargetApiInstanceId, Is.EqualTo("green-instance"));
        Assert.That(fixture.Publisher.TargetApiColor, Is.EqualTo("green"));
    }

    [Test]
    public async Task RequestForApiColor_RejectsCutoverWhenConnectedRunnerCannotAcceptCommand()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync(previousRunnerProtocol: "4");
        var result = await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);

        Assert.That(result.RequestedCount, Is.Zero);
        Assert.That(result.FailedCount, Is.EqualTo(1));
        Assert.That(fixture.Publisher.RunnerIds, Is.Empty);
    }

    [Test]
    public async Task PartialCutoverPublication_RollbackQueuesCompensationForAllConnectedCompatibleRunners()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync(additionalPreviousRunner: true);
        fixture.Publisher.FailAfterRunnerCount = 1;

        var forwardResult = await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);
        Assert.That(forwardResult.FailedCount, Is.EqualTo(5));
        Assert.That(fixture.Publisher.Publications, Has.Count.EqualTo(1));
        Assert.That(fixture.Publisher.Publications[0].RunnerIds, Has.Count.EqualTo(1));
        await fixture.DisconnectRunnerAsync(fixture.Publisher.Publications[0].RunnerIds.Single());

        fixture.Publisher.FailAfterRunnerCount = null;
        var rollbackResult = await fixture.Service.RequestForApiColorAsync(
            "blue", reconcileAllRegisteredRunners: true, CancellationToken.None);

        Assert.That(rollbackResult.FailedCount, Is.Zero);
        Assert.That(rollbackResult.RequestedCount, Is.EqualTo(5));
        Assert.That(fixture.Publisher.Publications, Has.Count.EqualTo(2));
        Assert.That(fixture.Publisher.Publications[1].RunnerIds,
            Is.EquivalentTo(new[] { fixture.PreviousRunnerId, fixture.AdditionalPreviousRunnerId,
                fixture.CurrentRunnerId, fixture.CurrentReplicaRunnerId, fixture.DisconnectedRunnerId }));
        Assert.That(fixture.Publisher.Publications[1].TargetApiColor, Is.EqualTo("blue"));
        Assert.That(fixture.Publisher.Publications[1].TargetApiInstanceId, Is.Null);
    }

    [Test]
    public async Task RapidRollbackRollforward_AppendsLatestTargetDespiteUnchangedPresenceAndColor()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync();
        await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);
        await fixture.Service.RequestForApiColorAsync("blue", true, CancellationToken.None);
        await fixture.DisconnectRunnerAsync(fixture.CurrentReplicaRunnerId);

        var final = await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);

        Assert.That(final.RequestedCount, Is.EqualTo(4));
        Assert.That(final.FailedCount, Is.Zero);
        foreach (var runnerId in new[] { fixture.PreviousRunnerId, fixture.CurrentRunnerId,
            fixture.CurrentReplicaRunnerId, fixture.DisconnectedRunnerId })
        {
            Assert.That(fixture.Publisher.Publications.Where(item => item.RunnerIds.Contains(runnerId))
                .Select(item => item.TargetApiColor), Is.EqualTo(new[] { "green", "blue", "green" }));
        }
    }
}

internal sealed class RunnerTunnelReconnectFixture : IAsyncDisposable
{
    private const string InstanceId = "green-instance";
    private readonly ApplicationDbContext db;
    private readonly string? previousInstanceId;
    private readonly string? previousApiColor;

    private RunnerTunnelReconnectFixture(
        ApplicationDbContext db,
        RunnerTunnelReconnectService service,
        RecordingReconnectPublisher publisher,
        Guid previousRunnerId,
        Guid additionalPreviousRunnerId,
        Guid currentRunnerId,
        Guid currentReplicaRunnerId,
        Guid disconnectedRunnerId,
        string? previousInstanceId,
        string? previousApiColor)
    {
        this.db = db;
        this.previousInstanceId = previousInstanceId;
        this.previousApiColor = previousApiColor;
        Service = service;
        Publisher = publisher;
        PreviousRunnerId = previousRunnerId;
        AdditionalPreviousRunnerId = additionalPreviousRunnerId;
        CurrentRunnerId = currentRunnerId;
        CurrentReplicaRunnerId = currentReplicaRunnerId;
        DisconnectedRunnerId = disconnectedRunnerId;
    }

    public RunnerTunnelReconnectService Service { get; }
    public RecordingReconnectPublisher Publisher { get; }
    public Guid PreviousRunnerId { get; }
    public Guid AdditionalPreviousRunnerId { get; }
    public Guid CurrentRunnerId { get; }
    public Guid CurrentReplicaRunnerId { get; }
    public Guid DisconnectedRunnerId { get; }

    public async Task<Guid> AddInfrastructureRunnerAsync(bool isActive)
    {
        var key = new ApiKey { Id = Guid.NewGuid(), IsActive = isActive };
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = isActive ? "eligible-offline" : "retained-revoked",
            Purpose = RunnerPurposes.Infrastructure, ConnectionId = null!,
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current,
            ApiKeyId = key.Id, ApiKey = key
        };
        db.Runners.Add(runner);
        await db.SaveChangesAsync();
        return runner.Id;
    }

    public async Task DisconnectRunnerAsync(Guid runnerId)
    {
        var runner = await db.Runners.SingleAsync(candidate => candidate.Id == runnerId);
        runner.ConnectionId = null!;
        await db.SaveChangesAsync();
    }

    public static async Task<RunnerTunnelReconnectFixture> CreateAsync(
        string previousRunnerProtocol = RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands,
        bool additionalPreviousRunner = false)
    {
        var oldInstanceId = Environment.GetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID");
        var oldApiColor = Environment.GetEnvironmentVariable("AUTOMATON_API_COLOR");
        Environment.SetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID", InstanceId);
        Environment.SetEnvironmentVariable("AUTOMATON_API_COLOR", "green");
        var db = CreateDb();
        var previousRunnerId = Guid.NewGuid();
        var additionalPreviousRunnerId = Guid.NewGuid();
        var currentRunnerId = Guid.NewGuid();
        var currentReplicaRunnerId = Guid.NewGuid();
        var disconnectedRunnerId = Guid.NewGuid();
        db.Runners.AddRange(
            ConnectedRunner(previousRunnerId, "previous", "blue-instance", "blue", previousRunnerProtocol),
            ConnectedRunner(currentRunnerId, "current", InstanceId, "green", RunnerApiCompatibilityVersions.Current),
            ConnectedRunner(currentReplicaRunnerId, "current-replica", "green-instance-2", "green", RunnerApiCompatibilityVersions.Current),
            new Runner
            {
                Id = disconnectedRunnerId,
                Name = "disconnected-compatible",
                ConnectionId = null!,
                ApiInstanceId = "blue-instance-3",
                ApiColor = "blue",
                ApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current
            },
            new Runner { Id = Guid.NewGuid(), Name = "disconnected", ConnectionId = null!, ApiInstanceId = "blue-instance" });
        if (additionalPreviousRunner)
            db.Runners.Add(ConnectedRunner(additionalPreviousRunnerId, "previous-2", "blue-instance-2", "blue", previousRunnerProtocol));
        await db.SaveChangesAsync();

        var publisher = new RecordingReconnectPublisher();
        var service = new RunnerTunnelReconnectService(
            db,
            new ApiInstanceIdentityService(),
            publisher,
            NullLogger<RunnerTunnelReconnectService>.Instance);
        return new RunnerTunnelReconnectFixture(db, service, publisher, previousRunnerId,
            additionalPreviousRunnerId, currentRunnerId, currentReplicaRunnerId, disconnectedRunnerId,
            oldInstanceId, oldApiColor);
    }

    public ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID", previousInstanceId);
        Environment.SetEnvironmentVariable("AUTOMATON_API_COLOR", previousApiColor);
        return db.DisposeAsync();
    }

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Runner ConnectedRunner(Guid id, string name, string instanceId, string color, string protocol)
        => new()
        {
            Id = id,
            Name = name,
            ConnectionId = $"{name}-connection",
            ApiInstanceId = instanceId,
            ApiColor = color,
            ApiCompatibilityVersion = protocol
        };
}

internal sealed class RecordingReconnectPublisher : IRuntimeTunnelReconnectCommandPublisher
{
    public List<ReconnectPublication> Publications { get; } = [];
    public int? FailAfterRunnerCount { get; set; }
    public HashSet<Guid> RemovedQueueRunnerIds { get; } = [];

    public List<Guid> RunnerIds => Publications.SelectMany(publication => publication.RunnerIds).ToList();
    public string? TargetApiInstanceId => Publications.LastOrDefault()?.TargetApiInstanceId;
    public string? TargetApiColor => Publications.LastOrDefault()?.TargetApiColor;

    public Task<int> PublishRuntimeTunnelReconnectsAsync(
        IReadOnlyCollection<Guid> runnerIds,
        string? targetApiInstanceId,
        string? targetApiColor,
        CancellationToken cancellationToken)
    {
        if (runnerIds.Any(RemovedQueueRunnerIds.Contains))
            throw new InvalidOperationException("Mandatory publication to a removed Runner queue.");
        var publishedRunnerIds = FailAfterRunnerCount is { } failAfter
            ? runnerIds.Take(failAfter).ToArray()
            : runnerIds.ToArray();
        Publications.Add(new ReconnectPublication(publishedRunnerIds, targetApiInstanceId, targetApiColor));
        if (FailAfterRunnerCount is not null)
            throw new InvalidOperationException("Simulated partial command publication.");

        return Task.FromResult(runnerIds.Count);
    }
}

internal sealed record ReconnectPublication(
    IReadOnlyCollection<Guid> RunnerIds,
    string? TargetApiInstanceId,
    string? TargetApiColor);
