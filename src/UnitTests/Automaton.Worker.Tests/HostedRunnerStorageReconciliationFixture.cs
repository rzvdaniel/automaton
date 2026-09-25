using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Tests;

internal sealed class HostedRunnerStorageReconciliationFixture : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;
    internal ApplicationDbContext Db { get; }
    internal HostedRunnerStorageReconciliationService Service { get; }

    private HostedRunnerStorageReconciliationFixture(
        ServiceProvider provider,
        IServiceScope scope,
        ApplicationDbContext db,
        HostedRunnerStorageReconciliationService service)
    {
        this.provider = provider;
        this.scope = scope;
        Db = db;
        Service = service;
    }

    internal static async Task<HostedRunnerStorageReconciliationFixture> CreateAsync(
        string state,
        DateTime measuredAt,
        StorageTelemetryOptions? telemetryOptions = null)
    {
        var context = CreateContext();
        AddRunner(context.Db, state, measuredAt);
        await context.Db.SaveChangesAsync();
        var service = CreateService(context.Provider, telemetryOptions);
        return new HostedRunnerStorageReconciliationFixture(context.Provider, context.Scope, context.Db, service);
    }

    private static (ServiceProvider Provider, IServiceScope Scope, ApplicationDbContext Db) CreateContext()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (provider, scope, db);
    }

    private static void AddRunner(ApplicationDbContext db, string state, DateTime measuredAt)
        => db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "runner", ContainerName = "runner",
            StorageState = state, StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
            StorageMeasuredAtUtc = measuredAt, StorageVerifiedAtUtc = DateTime.UtcNow
        });

    private static HostedRunnerStorageReconciliationService CreateService(
        ServiceProvider provider,
        StorageTelemetryOptions? telemetryOptions)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), new NoOpPublisher(),
            new NoOpTransactionalEventExecutor(), Options.Create(telemetryOptions ?? new StorageTelemetryOptions()));

    public ValueTask DisposeAsync()
    {
        scope.Dispose();
        return provider.DisposeAsync();
    }

    private sealed class NoOpPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
            => Task.CompletedTask;
    }

    private sealed class NoOpTransactionalEventExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
