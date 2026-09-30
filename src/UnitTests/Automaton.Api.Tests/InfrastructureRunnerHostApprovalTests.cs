using Automaton.Api.Config;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Tests;

public sealed class InfrastructureRunnerHostApprovalTests
{
    [Test]
    public async Task HostApproval_EnablesSchedulingAfterValidationButStorageTelemetryIsStillRequired()
    {
        await using var db = CreateDbContext();
        var host = await SeedApprovalHostAsync(db, "8.8.8.8");
        var service = CreateApprovalService(db);

        var approved = await service.ApproveAsync(host.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.That(approved.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.Ready));
        Assert.That(approved.HostedRunnerSchedulingEnabled, Is.True);
        Assert.That(approved.ApprovedAtUtc, Is.Not.Null);
        Assert.That(approved.ApprovedByUserId, Is.Not.Null);
        Assert.That(approved.IsSchedulingEligible, Is.False);
    }

    [Test]
    public async Task HostApproval_RejectsPublicConnectivityWithoutPublicAddress()
    {
        await using var db = CreateDbContext();
        var host = await SeedApprovalHostAsync(db, publicIp: null);
        var service = CreateApprovalService(db);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(host.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("public IPv4"));
        Assert.That(host.HostedRunnerSchedulingEnabled, Is.False);
    }

    private static async Task<RunnerHost> SeedApprovalHostAsync(ApplicationDbContext db, string? publicIp)
    {
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "infra",
            HashedKey = "hash",
            KeyPrefix = "ak_runner_test",
            CreatedAt = DateTime.UtcNow
        };
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            ApiKey = apiKey,
            ApiKeyId = apiKey.Id,
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = publicIp,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
            ProvisioningStatus = RunnerHostProvisioningStatuses.AwaitingApproval,
            ValidatedAtUtc = DateTime.UtcNow,
            HostedRunnerSchedulingEnabled = false,
            InfrastructureRunner = runner,
            InfrastructureRunnerId = runner.Id
        };
        db.AddRange(apiKey, runner, host);
        await db.SaveChangesAsync();
        return host;
    }

    private static InfrastructureRunnerHostApprovalService CreateApprovalService(ApplicationDbContext db)
        => new(
            db,
            new RecordingAuditEventService(),
            NullLogger<InfrastructureRunnerHostApprovalService>.Instance,
            new NullHubContext<AutomatonHub>(), RunnerExecutionTestServices.Revocation(db));

    private sealed class NullHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }

    private sealed class NullHubClients : IHubClients
    {
        public IClientProxy All => new NullClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new NullClientProxy();
        public IClientProxy Client(string connectionId) => new NullClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new NullClientProxy();
        public IClientProxy Group(string groupName) => new NullClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new NullClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new NullClientProxy();
        public IClientProxy User(string userId) => new NullClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new NullClientProxy();
    }

    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"infrastructure-workflow-{Guid.NewGuid()}")
            .Options);

    private sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
