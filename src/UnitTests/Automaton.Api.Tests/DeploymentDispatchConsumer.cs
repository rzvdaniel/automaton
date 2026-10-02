using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Messaging;
using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using CapHeaders = DotNetCore.CAP.Messages.Headers;

namespace Automaton.Api.Tests;

// Exercises CAP's actual serial RabbitMQ delivery callback without a live broker.
internal sealed class DeploymentDispatchConsumer : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly DeploymentOperationDispatcher dispatcher;
    private readonly RabbitMqBasicConsumer consumer;
    private readonly Func<RunnerExecutionRequestedEvent, Task> completeManagement;
    private readonly CancellationToken token;

    public DeploymentDispatchConsumer(ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        DeploymentDispatchProbe probe, Func<RunnerExecutionRequestedEvent, Task> completeManagement, CancellationToken token)
        : this(new ServiceCollection().AddLogging()
            .AddScoped(_ => DeploymentDispatchServices.CreateCallbackContext(fixture.Db))
            .AddScoped(sp => DeploymentDispatchServices.CreateOperations(fixture, probe,
                sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<IServiceScopeFactory>())),
            completeManagement, token)
    { }

    public DeploymentDispatchConsumer(IServiceCollection registrations,
        Func<RunnerExecutionRequestedEvent, Task> completeManagement, CancellationToken token)
    {
        services = registrations
            .AddSingleton<DeploymentOperationDispatcher>()
            .AddTransient<DeploymentPackageRequestedCapSubscriber>().BuildServiceProvider();
        dispatcher = services.GetRequiredService<DeploymentOperationDispatcher>();
        this.completeManagement = completeManagement;
        this.token = token;
        consumer = new RabbitMqBasicConsumer(null!, 0, "automaton.api.v1", ConsumeAsync, _ => { }, null, services);
    }

    public Task StartAsync() => dispatcher.StartAsync(token);
    public Task StopAsync() => dispatcher.StopAsync(CancellationToken.None);
    public Task DeliverDeploymentAsync(Guid id) => DeliverAsync(EventTopics.DeploymentPackageRequested,
        new DeploymentPackageRequestedEvent { OperationId = id });
    public Task DeliverManagementAsync(RunnerExecutionRequestedEvent message)
        => DeliverAsync(EventTopics.RunnerExecutionRequested, message);
    public ValueTask DisposeAsync() => services.DisposeAsync();

    private async Task ConsumeAsync(TransportMessage message, object? sender)
    {
        if (message.Headers[CapHeaders.MessageName] == EventTopics.DeploymentPackageRequested)
        {
            using var scope = services.CreateScope();
            var notification = JsonSerializer.Deserialize<DeploymentPackageRequestedEvent>(message.Body.Span)!;
            await scope.ServiceProvider.GetRequiredService<DeploymentPackageRequestedCapSubscriber>()
                .HandleAsync(notification, token);
        }
        else await completeManagement(JsonSerializer.Deserialize<RunnerExecutionRequestedEvent>(message.Body.Span)!);
    }

    private Task DeliverAsync<T>(string topic, T message) => consumer.HandleBasicDeliverAsync("test", 1, false, "", topic,
        new BasicProperties { Headers = new Dictionary<string, object?> { [CapHeaders.MessageName] = topic } },
        JsonSerializer.SerializeToUtf8Bytes(message), token).WaitAsync(TimeSpan.FromSeconds(5), token);
}
