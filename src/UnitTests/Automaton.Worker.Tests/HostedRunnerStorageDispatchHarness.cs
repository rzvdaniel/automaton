using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Tests;

internal sealed class HostedRunnerStorageDispatchHarness : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly HostedRunnerStorageReconciliationService service;
    private readonly RecordingPublisher publisher;
    private readonly string databaseName;
    private Guid hostId;
    private Guid infrastructureRunnerId;
    private Guid runnerId;

    private HostedRunnerStorageDispatchHarness(
        ServiceProvider provider,
        HostedRunnerStorageReconciliationService service,
        RecordingPublisher publisher,
        string databaseName)
    {
        this.provider = provider;
        this.service = service;
        this.publisher = publisher;
        this.databaseName = databaseName;
    }

    internal static Task<HostedRunnerStorageDispatchHarness> CreateAsync(int maxPerHost = 1, int gracePeriodHours = 24)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        var publisher = new RecordingPublisher();
        var service = new HostedRunnerStorageReconciliationService(
            provider.GetRequiredService<IServiceScopeFactory>(), publisher,
            new NoOpTransactionalEventExecutor(), Options.Create(new StorageTelemetryOptions
            {
                MaxVerificationDispatchesPerHost = maxPerHost,
                OverLimitGracePeriodHours = gracePeriodHours
            }));
        return Task.FromResult(new HostedRunnerStorageDispatchHarness(provider, service, publisher, databaseName));
    }

    internal async Task<Guid> SeedRunnerAsync(bool provisioning = false, int storageProjectId = 1_234_567)
    {
        hostId = Guid.NewGuid();
        infrastructureRunnerId = Guid.NewGuid();
        runnerId = Guid.NewGuid();
        await using var db = await OpenDbAsync();
        db.RunnerHosts.Add(new RunnerHost
        {
            Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = infrastructureRunnerId
        });
        db.HostedRunners.Add(new HostedRunner
        {
            Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner",
            RunnerHostId = hostId, Status = provisioning ? HostedRunnerStatuses.Provisioning : HostedRunnerStatuses.Running,
            ContainerId = provisioning ? null : "container", StorageProjectId = storageProjectId,
            StorageState = HostedRunnerStorageStates.Healthy,
            StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
            StorageSelfReportedStatus = StorageMeasurementStatuses.Succeeded,
            StorageMeasuredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return runnerId;
    }

    internal async Task<Guid> AddVerificationAsync(string status, string reason, DateTime? requestedAt = null)
    {
        var id = Guid.NewGuid();
        await using var db = await OpenDbAsync();
        db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
        {
            Id = id, HostedRunnerId = runnerId, IdempotencyKey = id.ToString("N"), Status = status,
            Reason = reason, RequestedAtUtc = requestedAt ?? DateTime.UtcNow,
            StartedAtUtc = status is StorageVerificationStatuses.Dispatching or StorageVerificationStatuses.Running
                ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
        return id;
    }

    internal async Task SeedOverLimitRunnerAsync()
    {
        hostId = Guid.NewGuid();
        infrastructureRunnerId = Guid.NewGuid();
        runnerId = Guid.NewGuid();
        await using var db = await OpenDbAsync();
        db.RunnerHosts.Add(new RunnerHost { Id = hostId, Name = "host", Provider = "test", InfrastructureRunnerId = infrastructureRunnerId });
        db.HostedRunners.Add(new HostedRunner
        {
            Id = runnerId, UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner", RunnerHostId = hostId,
            StorageAllowanceBytes = 100, StorageTotalBytes = 120, StorageState = HostedRunnerStorageStates.OverLimit,
            StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified,
            StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded, StorageMeasuredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    internal async Task AddDeploymentMirrorsAsync(int count)
    {
        await using var db = await OpenDbAsync();
        for (var index = 0; index < count; index++)
            db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
            {
                Id = Guid.NewGuid(), HostedRunnerId = runnerId, Action = HostedRunnerDeploymentActions.VerifyStorage,
                Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = DateTime.UtcNow.AddDays(-50 - index)
            });
        await db.SaveChangesAsync();
    }

    internal Task ReconcileAsync()
        => service.ReconcileAsync(default, includeHostedRunners: true, includeHosts: false);

    internal Task EnforceAsync() => service.EnforceAsync(default);

    internal async Task EndGracePeriodAsync()
    {
        await using var db = await OpenDbAsync();
        var runner = await db.HostedRunners.SingleAsync(x => x.Id == runnerId);
        runner.GracePeriodEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    internal async Task<string?> EnforcementStateAsync()
    {
        await using var db = await OpenDbAsync();
        return await db.HostedRunners.Where(x => x.Id == runnerId).Select(x => x.EnforcementState).SingleAsync();
    }

    internal async Task<int> SuspensionDeploymentCountAsync()
    {
        await using var db = await OpenDbAsync();
        return await db.HostedRunnerDeployments.CountAsync(x => x.HostedRunnerId == runnerId
            && x.Action == HostedRunnerDeploymentActions.Suspend);
    }

    internal InfrastructureCommandRequestedEvent SingleEnforcementCommand()
        => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();

    internal async Task<string> VerificationStatusAsync(Guid id)
    {
        await using var db = await OpenDbAsync();
        return await db.HostedRunnerStorageVerifications.Where(x => x.Id == id).Select(x => x.Status).SingleAsync();
    }

    internal async Task<int> PendingVerificationCountAsync()
    {
        await using var db = await OpenDbAsync();
        return await db.HostedRunnerStorageVerifications.CountAsync(x => x.Status == StorageVerificationStatuses.Pending);
    }

    internal async Task<int> VerificationDeploymentCountAsync()
    {
        await using var db = await OpenDbAsync();
        return await db.HostedRunnerDeployments.CountAsync(x => x.Action == HostedRunnerDeploymentActions.VerifyStorage);
    }

    internal InfrastructureCommandRequestedEvent SingleCommand()
        => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();

    internal int CommandCount => publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Count();

    internal Guid HostId => hostId;
    internal Guid InfrastructureRunnerId => infrastructureRunnerId;

    private Task<ApplicationDbContext> OpenDbAsync()
        => Task.FromResult(new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName).Options));

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    private sealed class RecordingPublisher : IEventPublisher
    {
        internal List<object> Messages { get; } = [];
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpTransactionalEventExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
