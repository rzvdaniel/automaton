using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerTunnelReconnectServiceTests
{
    [Test]
    public async Task RequestForApiColor_QueuesReconnectOnlyForConnectedRunnersOutsideTargetColor()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync();
        var result = await fixture.Service.RequestForApiColorAsync("green", false, CancellationToken.None);

        Assert.That(result.ConnectedRunnerCount, Is.EqualTo(1));
        Assert.That(result.RequestedCount, Is.EqualTo(1));
        Assert.That(result.FailedCount, Is.Zero);
        Assert.That(fixture.Publisher.RunnerIds, Is.EqualTo(new[] { fixture.PreviousRunnerId }));
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
        Assert.That(forwardResult.FailedCount, Is.EqualTo(2));
        Assert.That(fixture.Publisher.Publications, Has.Count.EqualTo(1));
        Assert.That(fixture.Publisher.Publications[0].RunnerIds, Has.Count.EqualTo(1));

        fixture.Publisher.FailAfterRunnerCount = null;
        var rollbackResult = await fixture.Service.RequestForApiColorAsync(
            "blue", includeRunnersAlreadyOnTargetColor: true, CancellationToken.None);

        Assert.That(rollbackResult.FailedCount, Is.Zero);
        Assert.That(rollbackResult.RequestedCount, Is.EqualTo(4));
        Assert.That(fixture.Publisher.Publications, Has.Count.EqualTo(2));
        Assert.That(fixture.Publisher.Publications[1].RunnerIds,
            Is.EquivalentTo(new[] { fixture.PreviousRunnerId, fixture.AdditionalPreviousRunnerId,
                fixture.CurrentRunnerId, fixture.CurrentReplicaRunnerId }));
        Assert.That(fixture.Publisher.Publications[1].TargetApiColor, Is.EqualTo("blue"));
        Assert.That(fixture.Publisher.Publications[1].TargetApiInstanceId, Is.Null);
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
    }

    public RunnerTunnelReconnectService Service { get; }
    public RecordingReconnectPublisher Publisher { get; }
    public Guid PreviousRunnerId { get; }
    public Guid AdditionalPreviousRunnerId { get; }
    public Guid CurrentRunnerId { get; }
    public Guid CurrentReplicaRunnerId { get; }

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
        db.Runners.AddRange(
            ConnectedRunner(previousRunnerId, "previous", "blue-instance", "blue", previousRunnerProtocol),
            ConnectedRunner(currentRunnerId, "current", InstanceId, "green", RunnerApiCompatibilityVersions.Current),
            ConnectedRunner(currentReplicaRunnerId, "current-replica", "green-instance-2", "green", RunnerApiCompatibilityVersions.Current),
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
            additionalPreviousRunnerId, currentRunnerId, currentReplicaRunnerId, oldInstanceId, oldApiColor);
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

    public List<Guid> RunnerIds => Publications.SelectMany(publication => publication.RunnerIds).ToList();
    public string? TargetApiInstanceId => Publications.LastOrDefault()?.TargetApiInstanceId;
    public string? TargetApiColor => Publications.LastOrDefault()?.TargetApiColor;

    public Task<int> PublishRuntimeTunnelReconnectsAsync(
        IReadOnlyCollection<Guid> runnerIds,
        string? targetApiInstanceId,
        string? targetApiColor,
        CancellationToken cancellationToken)
    {
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
