using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

public sealed class InfrastructureCommandConsumer(
    InfrastructureCommandIdentity identity,
    IInfrastructureActionExecutor executor,
    IInfrastructureCommandResultClient results)
{
    public InfrastructureCommandConsumer(InfrastructureCommandIdentity identity, IInfrastructureActionExecutor executor)
        : this(identity, executor, new MissingInfrastructureCommandResultClient())
    {
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string QueueName => $"automaton.infrastructure.{identity.RunnerHostId:N}";

    public async Task<InfrastructureCommandExecutionResult> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(body.Span, JsonOptions)
            ?? throw new InvalidOperationException("Infrastructure command body is required.");
        if (identity.RunnerHostId == Guid.Empty || identity.RunnerId == Guid.Empty
            || command.CommandId == Guid.Empty || command.OperationId == Guid.Empty
            || command.RunnerHostId != identity.RunnerHostId || command.InfrastructureRunnerId != identity.RunnerId)
            throw new UnauthorizedAccessException("Infrastructure command identity does not match this Runner and Runner Host.");
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        if (command.SecretGrantIds == null || string.IsNullOrWhiteSpace(command.InputJson)
            || command.CreatedAtUtc == default || command.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Infrastructure command input, grant IDs, and UTC creation time are required.");
        return await executor.ExecuteAsync(command, cancellationToken);
    }

    public async Task<InfrastructureCommandExecutionResult> ConsumeOneAsync(
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Require(configuration, "RabbitMq:Host"),
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = Require(configuration, "RabbitMq:Username"),
            Password = Require(configuration, "RabbitMq:Password"),
            VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = false
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var exchange = configuration["RabbitMq:ExchangeName"] ?? "cap.default.router";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(QueueName, exchange, EventTopics.InfrastructureCommandRequested, cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken);
        var completion = new TaskCompletionSource<InfrastructureCommandExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var result = await HandleAsync(delivery.Body, cancellationToken);
                var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(delivery.Body.Span, JsonOptions)!;
                if (result.Terminal)
                {
                    Serilog.Log.Warning(
                        "Infrastructure command {CommandId} for operation {OperationId} ({ActionKey}) was terminalized before Action execution: {TerminalReason}",
                        command.CommandId, command.OperationId, command.ActionKey, result.TerminalReason);
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                    completion.TrySetResult(result);
                    return;
                }
                await results.SubmitAsync(command, result.Execution!, result.Result!, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
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
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task ConsumeAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Require(configuration, "RabbitMq:Host"),
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = Require(configuration, "RabbitMq:Username"),
            Password = Require(configuration, "RabbitMq:Password"),
            VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var exchange = configuration["RabbitMq:ExchangeName"] ?? "cap.default.router";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(QueueName, exchange, EventTopics.InfrastructureCommandRequested, cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(delivery.Body.Span, JsonOptions)!;
                var result = await HandleAsync(delivery.Body, cancellationToken);
                if (result.Terminal)
                {
                    Serilog.Log.Warning(
                        "Infrastructure command {CommandId} for operation {OperationId} ({ActionKey}) was terminalized before Action execution: {TerminalReason}",
                        command.CommandId, command.OperationId, command.ActionKey, result.TerminalReason);
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                    return;
                }
                await results.SubmitAsync(command, result.Execution!, result.Result!, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (UnauthorizedAccessException exception) when (IsWrongAddress(exception))
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            }
            catch
            {
                // Leave the delivery unacknowledged; closing the channel makes RabbitMQ redeliver it.
                stopped.TrySetException(new InvalidOperationException("Infrastructure command callback failed; the delivery was left unacknowledged."));
                await channel.CloseAsync(cancellationToken);
            }
        };
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken);
        await stopped.Task.WaitAsync(cancellationToken);
    }

    private static string Require(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required for the Infrastructure Runner consumer.");

    private static bool IsWrongAddress(UnauthorizedAccessException exception) =>
        exception.Message.Contains("does not match this Runner", StringComparison.Ordinal);

    private sealed class MissingInfrastructureCommandResultClient : IInfrastructureCommandResultClient
    {
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Infrastructure command result delivery is required before acknowledgement.");
    }
}
