using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class InfrastructureCommandQueueConsumer(
    string queueName,
    Func<ReadOnlyMemory<byte>, CancellationToken, Task<InfrastructureCommandExecutionResult>> handle,
    IInfrastructureCommandResultClient results,
    Func<InfrastructureCommandRequestedEvent, InfrastructureCommandExecutionResult, CancellationToken, Task>? submitHostContainerResult = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<InfrastructureCommandExecutionResult> ConsumeOneAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var factory = CreateConnectionFactory(configuration, automaticRecovery: false);
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await ConfigureQueueAsync(channel, configuration, queueName, cancellationToken);
        var completion = new TaskCompletionSource<InfrastructureCommandExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var result = await handle(delivery.Body, cancellationToken);
                var command = DeserializeCommand(delivery.Body);
                await SubmitAndAcknowledgeAsync(command, result, channel, delivery.DeliveryTag, cancellationToken);
                completion.TrySetResult(result);
            }
            catch (UnauthorizedAccessException exception) when (IsWrongAddress(exception))
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                completion.TrySetException(exception);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        };
        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task ConsumeAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var factory = CreateConnectionFactory(configuration, automaticRecovery: true);
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await ConfigureQueueAsync(channel, configuration, queueName, cancellationToken);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var command = DeserializeCommand(delivery.Body);
                var result = await handle(delivery.Body, cancellationToken);
                await SubmitAndAcknowledgeAsync(command, result, channel, delivery.DeliveryTag, cancellationToken);
            }
            catch (UnauthorizedAccessException exception) when (IsWrongAddress(exception))
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (Exception exception)
            {
                // The owning ConsumeAsync scope disposes the channel after this callback returns.
                // Closing it here can wait for the same callback and prevent recovery.
                stopped.TrySetException(exception);
            }
        };
        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, cancellationToken);
        await stopped.Task.WaitAsync(cancellationToken);
    }

    private static ConnectionFactory CreateConnectionFactory(IConfiguration configuration, bool automaticRecovery)
        => new()
        {
            HostName = Require(configuration, "RabbitMq:Host"),
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = Require(configuration, "RabbitMq:Username"),
            Password = Require(configuration, "RabbitMq:Password"),
            VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = automaticRecovery,
            TopologyRecoveryEnabled = true
        };

    private static async Task ConfigureQueueAsync(
        IChannel channel,
        IConfiguration configuration,
        string queueName,
        CancellationToken cancellationToken)
    {
        var exchange = configuration["RabbitMq:ExchangeName"] ?? "cap.default.router";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueName, exchange, EventTopics.InfrastructureCommandRequested, cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken);
    }

    private async Task SubmitAndAcknowledgeAsync(
        InfrastructureCommandRequestedEvent command,
        InfrastructureCommandExecutionResult result,
        IChannel channel,
        ulong deliveryTag,
        CancellationToken cancellationToken)
    {
        if (result.Terminal)
        {
            Serilog.Log.Warning(
                "Infrastructure command {CommandId} for operation {OperationId} ({ActionKey}) was terminalized before Action execution: {TerminalReason}",
                command.CommandId, command.OperationId, command.ActionKey, result.TerminalReason);
        }
        else if (result.HostContainerResult is not null)
        {
            if (submitHostContainerResult is null)
                throw new InvalidOperationException("Host-container result delivery is not configured.");
            await submitHostContainerResult(command, result, cancellationToken);
        }
        else
        {
            if (result.Execution is null || result.Result is null)
                throw new InvalidOperationException("The infrastructure command returned no Action result.");
            await results.SubmitAsync(command, result.Execution, result.Result, cancellationToken);
        }
        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
    }

    private static InfrastructureCommandRequestedEvent DeserializeCommand(ReadOnlyMemory<byte> body)
        => JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(body.Span, JsonOptions)
            ?? throw new InvalidOperationException("Infrastructure command body is required.");

    private static string Require(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required for the Infrastructure Runner consumer.");

    private static bool IsWrongAddress(UnauthorizedAccessException exception)
        => exception.Message.Contains("does not match this Runner", StringComparison.Ordinal);
}
