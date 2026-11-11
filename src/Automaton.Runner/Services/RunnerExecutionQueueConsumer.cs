using Automaton.Core.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionQueueConsumer(RunnerExecutionDeliveryHandler handler, HostContainerResultNotifier? results = null)
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
        var management = ConsumeQueueAsync(connection, broker.ManagementQueueName!, AppActionExecutionService.MaximumConcurrentExecutions, api, runnerId, lifetime.Token);
        // Results have their own channel and consumer, so a slow command never sits in front of a result.
        var result = results is not null && !string.IsNullOrWhiteSpace(broker.ResultQueueName)
            ? ConsumeResultsAsync(connection, broker.ResultQueueName!, results, runnerId, lifetime.Token)
            : Task.Delay(Timeout.Infinite, lifetime.Token);
        if (results is not null)
            results.ResultQueueAvailable = !string.IsNullOrWhiteSpace(broker.ResultQueueName);
        var first = await Task.WhenAny(execution, management, result);
        await lifetime.CancelAsync();
        try { await Task.WhenAll(execution, management, result); }
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

    private static async Task ConsumeResultsAsync(IConnection connection, string queue,
        HostContainerResultNotifier results, Guid runnerId, CancellationToken token)
    {
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: 1), token);
        await channel.BasicQosAsync(0, 32, false, token);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            HandleResultReady(delivery.Body.Span, runnerId, results);
            // Always acknowledged: a duplicate or unmatched message is dropped, a missed one is covered by the fallback read.
            await channel.BasicAckAsync(delivery.DeliveryTag, false, token);
        };
        // A consumer the broker cancels (queue deleted) ends the result consumer, so the whole consumer restarts and reconnects.
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.UnregisteredAsync += (_, _) => { cancelled.TrySetException(new InvalidOperationException("The Runner result queue consumer was cancelled by the broker.")); return Task.CompletedTask; };
        await channel.BasicConsumeAsync(queue, false, consumer, token);
        results.ConsumerConnected();
        await cancelled.Task.WaitAsync(token);
    }

    internal static bool HandleResultReady(ReadOnlySpan<byte> body, Guid runnerId, HostContainerResultNotifier results)
    {
        try
        {
            var message = System.Text.Json.JsonSerializer.Deserialize<RunnerExecutionResultReady>(body,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (message is null || message.OperationId == Guid.Empty || message.RunnerId != runnerId)
                return false;
            return results.Notify(message.OperationId);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
