using Automaton.Api.Config;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class InfrastructureRunnerHostApprovalTests
{
    [Test]
    public async Task HostApproval_RequiresValidatedOnlineInfrastructureRunner()
    {
        await using var db = CreateDbContext();
        var host = await SeedApprovalHostAsync(db, "8.8.8.8");
        var service = CreateApprovalService(db);

        var approved = await service.ApproveAsync(host.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.That(approved.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.Ready));
        Assert.That(approved.ApprovedAtUtc, Is.Not.Null);
        Assert.That(approved.ApprovedByUserId, Is.Not.Null);
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
            RunnerExecutionTestServices.Revocation(db));
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
