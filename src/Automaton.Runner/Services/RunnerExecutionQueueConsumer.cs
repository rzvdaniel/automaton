using Automaton.Core.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionQueueConsumer(RunnerExecutionDeliveryHandler handler)
{
    public async Task ConsumeAsync(HostedRunnerBrokerConfiguration broker, RunnerExecutionApiClient api,
        Guid runnerId, CancellationToken token)
    {
        var factory = RunnerBrokerConnectionFactory.Create(broker, requireTls: true);
        await using var connection = await factory.CreateConnectionAsync(token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: token);
        await channel.BasicQosAsync(0, 1, false, token);
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
        await channel.BasicConsumeAsync(broker.QueueName, false, consumer, token);
        // Disposal happens after the delivery callback returns, never inside it.
        await stopped.Task.WaitAsync(token);
    }

}
