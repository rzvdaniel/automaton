using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal sealed class DeploymentDispatchConcurrencyScenario : IAsyncDisposable
{
    private readonly ApplicationDeploymentServiceTests.DeploymentFixture fixture;
    private readonly DeploymentDispatchConsumer consumer;
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
    public DeploymentDispatchTracker Tracker { get; } = new();

    private DeploymentDispatchConcurrencyScenario(ApplicationDeploymentServiceTests.DeploymentFixture fixture)
    {
        this.fixture = fixture;
        var registrations = new ServiceCollection().AddLogging()
            .AddSingleton<IOptions<DeploymentDispatchOptions>>(Options.Create(new DeploymentDispatchOptions { MaximumConcurrentOperations = 2 }))
            .AddScoped(_ => DeploymentDispatchServices.CreateCallbackContext(fixture.Db))
            .AddScoped<IHostedRunnerDeploymentCommandDispatcher>(sp => new ConcurrentDeploymentCommandProbe(
                sp.GetRequiredService<ApplicationDbContext>(), Tracker))
            .AddScoped(sp => DeploymentDispatchServices.CreateOperations(fixture,
                sp.GetRequiredService<IHostedRunnerDeploymentCommandDispatcher>(), sp.GetRequiredService<ApplicationDbContext>(),
                sp.GetRequiredService<IServiceScopeFactory>()));
        consumer = new(registrations, _ => throw new InvalidOperationException("Unexpected management delivery."), lifetime.Token);
    }

    public static async Task<DeploymentDispatchConcurrencyScenario> CreateAsync()
        => new(await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync());

    public Task StartAsync() => consumer.StartAsync();
    public Task StopAsync() => consumer.StopAsync();
    public Task NotifyAsync(Guid id) => consumer.DeliverDeploymentAsync(id);

    public Task<Guid> AddDeploymentAsync() => DeploymentDispatchTestData.AddAsync(fixture);

    public Task<DeploymentDispatchObservation> WaitForPublicationAsync(Guid id)
        => Tracker.Publication(id).WaitAsync(lifetime.Token);

    public async Task<string> StatusAsync(Guid id)
        => await fixture.Db.DeploymentOperations.AsNoTracking().Where(item => item.Id == id)
            .Select(item => item.Status).SingleAsync(lifetime.Token);

    public async Task CompleteAsync(Guid id)
    {
        await using var db = DeploymentDispatchServices.CreateCallbackContext(fixture.Db);
        var operation = await db.DeploymentOperations.SingleAsync(item => item.Id == id, lifetime.Token);
        var target = await db.AppDeploymentTargets.Include(item => item.ExecutionRunner)
            .SingleAsync(item => item.WebAppId == operation.WebAppId && item.IsDefault, lifetime.Token);
        var disposition = await DeploymentDispatchCompletionServices.Create(db, fixture.RootPath).ApplyAsync(
            target.ExecutionRunner!.ApiKeyId!.Value, id,
            new(operation.ExecutionAttemptId!.Value, new() { DeploymentTargetId = target.Id, Success = true }), lifetime.Token);
        Assert.That(disposition, Is.EqualTo("accepted"));
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await consumer.StopAsync();
        await consumer.DisposeAsync();
        lifetime.Dispose();
        await fixture.DisposeAsync();
    }
}
