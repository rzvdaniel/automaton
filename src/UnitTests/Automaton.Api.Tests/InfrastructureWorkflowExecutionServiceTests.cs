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

public sealed class InfrastructureWorkflowExecutionServiceTests
{
    [Test]
    public async Task HostValidation_PersistsResultLogsAndTargetIdentity()
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        var executor = new RecordingPublisher();
        var service = CreateWorkflowExecutionService(db, executor, "test-network");

        var response = await service.StartHostValidationAsync(
            new StartHostValidationRequest { RunnerHostId = host.Id, IdempotencyKey = "validation-1" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.That(response.Status, Is.EqualTo(InfrastructureWorkflowExecutionStatuses.Queued));
        Assert.That(host.ValidatedAtUtc, Is.Null);
        var command = executor.LastCommand!;
        Assert.That(command.InfrastructureRunnerId, Is.EqualTo(host.InfrastructureRunnerId));
        Assert.That(command.CommandId, Is.EqualTo(response.Id));
        Assert.That(command.InputJson, Does.Contain("test-network"));
        var callback = Result(command);
        var results = new InfrastructureHostValidationResultService(db, new RecordingAuditEventService());
        Assert.That((await results.ApplyAsync(host.InfrastructureRunner!.ApiKeyId!.Value, response.Id,
            callback, CancellationToken.None)).Outcome, Is.EqualTo("accepted"));
        Assert.That((await results.ApplyAsync(host.InfrastructureRunner.ApiKeyId.Value, response.Id,
            callback, CancellationToken.None)).Outcome, Is.EqualTo("already-applied"));
        await AssertValidationPersistenceAsync(db, host.Id);

    }

    private static async Task AssertValidationPersistenceAsync(ApplicationDbContext db, Guid hostId)
    {
        var validatedHost = await db.RunnerHosts.FindAsync(hostId);
        Assert.That(validatedHost!.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.AwaitingApproval));
        Assert.That(validatedHost.ValidatedAtUtc, Is.Not.Null);
        Assert.That(validatedHost.IsSchedulingEligible, Is.False);
        Assert.That(await db.InfrastructureWorkflowExecutionLogs.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task HostValidation_ReusesExecutionForIdempotencyKey()
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        var executor = new RecordingPublisher();
        var service = CreateWorkflowExecutionService(db, executor);
        var request = new StartHostValidationRequest { RunnerHostId = host.Id, IdempotencyKey = "same-request" };

        var first = await service.StartHostValidationAsync(request, Guid.NewGuid(), CancellationToken.None);
        var second = await service.StartHostValidationAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(executor.CallCount, Is.EqualTo(1));
        Assert.That(await db.InfrastructureWorkflowExecutions.CountAsync(), Is.EqualTo(1));
    }

    internal static async Task<RunnerHost> SeedValidationHostAsync(ApplicationDbContext db)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure),
            ApiKeyId = Guid.NewGuid(),
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            InfrastructureRunnerId = runner.Id,
            InfrastructureRunner = runner
        };
        db.AddRange(runner, host);
        await db.SaveChangesAsync();
        return host;
    }

    internal static InfrastructureWorkflowExecutionService CreateWorkflowExecutionService(
        ApplicationDbContext db,
        RecordingPublisher executor,
        string dockerNetwork = "")
        => new(
            db,
            executor,
            new InlineTransaction(),
            Options.Create(new HostedRunnerOptions { DockerNetwork = dockerNetwork }));

    internal static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"infrastructure-workflow-{Guid.NewGuid()}")
            .Options);

    internal sealed class RecordingPublisher : IEventPublisher
    {
        public int CallCount { get; private set; }
        public InfrastructureCommandRequestedEvent? LastCommand { get; private set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            CallCount++;
            LastCommand = (InfrastructureCommandRequestedEvent)(object)message;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineTransaction : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    internal static InfrastructureCommandResultRequest Result(InfrastructureCommandRequestedEvent command)
        => new(command.OperationId, command.RunnerHostId, command.InfrastructureRunnerId,
            command.ActionKey, [], new AppActionExecutionResult
            {
                ExecutionId = command.CommandId, Status = AppModuleExecutionStatuses.Succeeded,
                ExitCode = 0, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
            }, new AppModuleExecutionArtifactResponse(true, "{\"totalCpuCores\":2,\"totalMemoryMb\":4096,\"totalDiskMb\":40960,\"metadata\":{\"hostname\":\"host-test\"},\"health\":{\"docker\":\"Healthy\",\"network\":\"Healthy\",\"disk\":\"Healthy\"}}", null));

    [Test]
    public async Task HostValidation_RejectsWrongRunnerResultWithoutChangingHost()
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        var publisher = new RecordingPublisher();
        var service = CreateWorkflowExecutionService(db, publisher);
        await service.StartHostValidationAsync(new StartHostValidationRequest { RunnerHostId = host.Id },
            Guid.NewGuid(), CancellationToken.None);
        var command = publisher.LastCommand!;
        var results = new InfrastructureHostValidationResultService(db, new RecordingAuditEventService());
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => results.ApplyAsync(Guid.NewGuid(), command.CommandId,
            Result(command), CancellationToken.None));
        Assert.That(host.ValidatedAtUtc, Is.Null);
        Assert.That((await db.InfrastructureWorkflowExecutions.SingleAsync()).CompletedAtUtc, Is.Null);
    }

    [Test]
    public async Task HostValidation_FailedActionClosesOperationAndDisablesScheduling()
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        host.HostedRunnerSchedulingEnabled = true;
        var publisher = new RecordingPublisher();
        await CreateWorkflowExecutionService(db, publisher).StartHostValidationAsync(
            new StartHostValidationRequest { RunnerHostId = host.Id }, Guid.NewGuid(), CancellationToken.None);
        var command = publisher.LastCommand!;
        var request = Result(command);
        request.Execution.Status = AppModuleExecutionStatuses.Failed;
        request.Execution.FailureSummary = "Docker unavailable";
        await new InfrastructureHostValidationResultService(db, new RecordingAuditEventService())
            .ApplyAsync(host.InfrastructureRunner!.ApiKeyId!.Value, command.CommandId, request, CancellationToken.None);
        Assert.That(host.HostedRunnerSchedulingEnabled, Is.False);
        Assert.That(host.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.ValidationFailed));
        await AssertFailedExecutionAsync(db);
    }

    private static async Task AssertFailedExecutionAsync(ApplicationDbContext db)
        => Assert.That((await db.InfrastructureWorkflowExecutions.SingleAsync()).Status,
            Is.EqualTo(InfrastructureWorkflowExecutionStatuses.Failed));

    [Test]
    public async Task HostValidation_SupersededResultCannotUpdateHostSnapshot()
    {
        await using var db = CreateDbContext();
        var host = await SeedValidationHostAsync(db);
        var publisher = new RecordingPublisher();
        var service = CreateWorkflowExecutionService(db, publisher);
        await service.StartHostValidationAsync(new() { RunnerHostId = host.Id }, Guid.NewGuid(), CancellationToken.None);
        var older = publisher.LastCommand!;
        await service.StartHostValidationAsync(new() { RunnerHostId = host.Id }, Guid.NewGuid(), CancellationToken.None);
        var newer = publisher.LastCommand!;
        var results = new InfrastructureHostValidationResultService(db, new RecordingAuditEventService());
        await results.ApplyAsync(host.InfrastructureRunner!.ApiKeyId!.Value, older.CommandId,
            Result(older), CancellationToken.None);
        Assert.That(host.ValidatedAtUtc, Is.Null);
        await results.ApplyAsync(host.InfrastructureRunner.ApiKeyId.Value, newer.CommandId,
            Result(newer), CancellationToken.None);
        Assert.That(host.ValidatedAtUtc, Is.Not.Null);
    }

    internal sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

}
