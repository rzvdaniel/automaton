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
    public async Task RequestForCurrentInstance_QueuesReconnectOnlyForConnectedPreviousInstanceRunners()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync();
        var result = await fixture.Service.RequestForPreviousInstancesAsync(CancellationToken.None);

        Assert.That(result.ConnectedRunnerCount, Is.EqualTo(1));
        Assert.That(result.RequestedCount, Is.EqualTo(1));
        Assert.That(result.FailedCount, Is.Zero);
        Assert.That(fixture.Publisher.RunnerIds, Is.EqualTo(new[] { fixture.PreviousRunnerId }));
        Assert.That(fixture.Publisher.TargetApiInstanceId, Is.EqualTo("green-instance"));
        Assert.That(fixture.Publisher.TargetApiColor, Is.EqualTo("green"));
    }

    [Test]
    public async Task RequestForPreviousInstances_RejectsCutoverWhenConnectedRunnerCannotAcceptCommand()
    {
        await using var fixture = await RunnerTunnelReconnectFixture.CreateAsync(previousRunnerProtocol: "4");
        var result = await fixture.Service.RequestForPreviousInstancesAsync(CancellationToken.None);

        Assert.That(result.RequestedCount, Is.Zero);
        Assert.That(result.FailedCount, Is.EqualTo(1));
        Assert.That(fixture.Publisher.RunnerIds, Is.Empty);
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
        string? previousInstanceId,
        string? previousApiColor)
    {
        this.db = db;
        this.previousInstanceId = previousInstanceId;
        this.previousApiColor = previousApiColor;
        Service = service;
        Publisher = publisher;
        PreviousRunnerId = previousRunnerId;
    }

    public RunnerTunnelReconnectService Service { get; }
    public RecordingReconnectPublisher Publisher { get; }
    public Guid PreviousRunnerId { get; }

    public static async Task<RunnerTunnelReconnectFixture> CreateAsync(
        string previousRunnerProtocol = RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands)
    {
        var oldInstanceId = Environment.GetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID");
        var oldApiColor = Environment.GetEnvironmentVariable("AUTOMATON_API_COLOR");
        Environment.SetEnvironmentVariable("AUTOMATON_API_INSTANCE_ID", InstanceId);
        Environment.SetEnvironmentVariable("AUTOMATON_API_COLOR", "green");
        var db = CreateDb();
        var previousRunnerId = Guid.NewGuid();
        db.Runners.AddRange(
            ConnectedRunner(previousRunnerId, "previous", "blue-instance", "blue", previousRunnerProtocol),
            ConnectedRunner(Guid.NewGuid(), "current", InstanceId, "green", RunnerApiCompatibilityVersions.Current),
            ConnectedRunner(Guid.NewGuid(), "current-replica", "green-instance-2", "green", RunnerApiCompatibilityVersions.Current),
            new Runner { Id = Guid.NewGuid(), Name = "disconnected", ConnectionId = null!, ApiInstanceId = "blue-instance" });
        await db.SaveChangesAsync();

        var publisher = new RecordingReconnectPublisher();
        var service = new RunnerTunnelReconnectService(
            db,
            new ApiInstanceIdentityService(),
            publisher,
            NullLogger<RunnerTunnelReconnectService>.Instance);
        return new RunnerTunnelReconnectFixture(db, service, publisher, previousRunnerId, oldInstanceId, oldApiColor);
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
    public List<Guid> RunnerIds { get; } = [];
    public string? TargetApiInstanceId { get; private set; }
    public string? TargetApiColor { get; private set; }

    public Task<int> PublishRuntimeTunnelReconnectsAsync(
        IReadOnlyCollection<Guid> runnerIds,
        string targetApiInstanceId,
        string? targetApiColor,
        CancellationToken cancellationToken)
    {
        RunnerIds.AddRange(runnerIds);
        TargetApiInstanceId = targetApiInstanceId;
        TargetApiColor = targetApiColor;
        return Task.FromResult(runnerIds.Count);
    }
}
