using Automaton.Core.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionQueueConsumer(RunnerExecutionDeliveryHandler handler)
{
    public async Task ConsumeAsync(RunnerBrokerConfiguration broker, RunnerExecutionApiClient api,
        Guid runnerId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(broker.ManagementQueueName))
            throw new InvalidOperationException("Runner execution requires a separate management queue.");
        var factory = RunnerBrokerConnectionFactory.Create(broker, requireTls: true);
        await using var connection = await factory.CreateConnectionAsync(token);
        await ConsumeAsync(connection, broker, api, runnerId, token);
    }

    internal async Task ConsumeAsync(IConnection connection, RunnerBrokerConfiguration broker,
        RunnerExecutionApiClient api, Guid runnerId, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var execution = ConsumeQueueAsync(connection, broker.QueueName, AppActionExecutionService.MaximumConcurrentExecutions,
            api, runnerId, lifetime.Token);
        var management = ConsumeQueueAsync(connection, broker.ManagementQueueName!, 1, api, runnerId, lifetime.Token);
        var first = await Task.WhenAny(execution, management);
        await lifetime.CancelAsync();
        try { await Task.WhenAll(execution, management); }
        catch
        {
            // Preserve the failing channel's exception rather than the sibling's cancellation.
            await first;
            throw;
        }
    }

    private async Task ConsumeQueueAsync(IConnection connection, string queue, ushort capacity,
        RunnerExecutionApiClient api, Guid runnerId, CancellationToken token)
    {
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: capacity), token);
        await channel.BasicQosAsync(0, capacity, false, token);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var acknowledge = await handler.HandleAsync(delivery.Body, delivery.BasicProperties.MessageId, api, runnerId, token);
                if (acknowledge) await channel.BasicAckAsync(delivery.DeliveryTag, false, token);
                else await channel.BasicRejectAsync(delivery.DeliveryTag, false, token);
            }
            catch (Exception exception) { stopped.TrySetException(exception); }
        };
        await channel.BasicConsumeAsync(queue, false, consumer, token);
        // Disposal happens after the delivery callback returns, never inside it.
        await stopped.Task.WaitAsync(token);
    }

}
