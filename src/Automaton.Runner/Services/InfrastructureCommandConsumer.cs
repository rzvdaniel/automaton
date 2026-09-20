using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

public sealed class InfrastructureCommandConsumer(
    InfrastructureCommandIdentity identity,
    IInfrastructureActionExecutor executor,
    IInfrastructureCommandResultClient results,
    InfrastructureCommandConsumerStatus status,
    ILogger logger)
{
    public InfrastructureCommandConsumer(InfrastructureCommandIdentity identity, IInfrastructureActionExecutor executor)
        : this(identity, executor, new MissingInfrastructureCommandResultClient(),
            new InfrastructureCommandConsumerStatus(), NullLogger.Instance)
    {
    }

    public InfrastructureCommandConsumer(InfrastructureCommandIdentity identity, IInfrastructureActionExecutor executor,
        IInfrastructureCommandResultClient results)
        : this(identity, executor, results, new InfrastructureCommandConsumerStatus(), NullLogger.Instance)
    {
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public string QueueName => $"automaton.infrastructure.{identity.RunnerHostId:N}";
    public InfrastructureCommandConsumerReadiness Readiness => status.Readiness;

    public async Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> HandleAsync(
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

    public async Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ConsumeOneAsync(
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
        var completion = new TaskCompletionSource<(AppActionExecutionResult, AppModuleExecutionArtifactResponse)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var result = await HandleAsync(delivery.Body, cancellationToken);
                var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(delivery.Body.Span, JsonOptions)!;
                await results.SubmitAsync(command, result.Execution, result.Result, cancellationToken);
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
        status.Set(InfrastructureCommandConsumerReadiness.Starting, "Creating RabbitMQ command-consumer attempt.");
        var factory = new ConnectionFactory
        {
            HostName = Require(configuration, "RabbitMq:Host"),
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = Require(configuration, "RabbitMq:Username"),
            Password = Require(configuration, "RabbitMq:Password"),
            VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false
        };

        IConnection? connection = null;
        IChannel? channel = null;
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var setupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setupCancellation.CancelAfter(SetupTimeout);
        var setupToken = setupCancellation.Token;

        Task SignalShutdownAsync(string resource, ShutdownEventArgs reason)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                var failure = new InfrastructureCommandConsumerStoppedException(
                    $"RabbitMQ {resource} shutdown: {reason.ReplyCode} {reason.ReplyText}");
                status.Set(InfrastructureCommandConsumerReadiness.NotReady, failure.Message);
                logger.LogWarning("Infrastructure command consumer {Resource} shutdown. ReplyCode={ReplyCode} ReplyText={ReplyText}",
                    resource, reason.ReplyCode, reason.ReplyText);
                stopped.TrySetResult(failure);
            }
            return Task.CompletedTask;
        }

        try
        {
            logger.LogInformation("Infrastructure command consumer connection creation starting. Queue={QueueName} VirtualHost={VirtualHost}",
                QueueName, factory.VirtualHost);
            connection = await factory.CreateConnectionAsync(setupToken);
            logger.LogInformation("Infrastructure command consumer connection opened. Queue={QueueName}", QueueName);
            connection.ConnectionShutdownAsync += (_, reason) => SignalShutdownAsync("connection", reason);

            logger.LogInformation("Infrastructure command consumer channel creation starting. Queue={QueueName}", QueueName);
            channel = await connection.CreateChannelAsync(cancellationToken: setupToken);
            logger.LogInformation("Infrastructure command consumer channel opened. Queue={QueueName}", QueueName);
            channel.ChannelShutdownAsync += (_, reason) => SignalShutdownAsync("channel", reason);

            var exchange = configuration["RabbitMq:ExchangeName"] ?? "cap.default.router";
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: setupToken);
            logger.LogInformation("Infrastructure command consumer exchange declaration complete. Exchange={ExchangeName}", exchange);
            await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: setupToken);
            logger.LogInformation("Infrastructure command consumer queue declaration complete. Queue={QueueName}", QueueName);
            await channel.QueueBindAsync(QueueName, exchange, EventTopics.InfrastructureCommandRequested, cancellationToken: setupToken);
            logger.LogInformation("Infrastructure command consumer binding complete. Queue={QueueName} RoutingKey={RoutingKey}",
                QueueName, EventTopics.InfrastructureCommandRequested);
            await channel.BasicQosAsync(0, 1, global: false, setupToken);
            logger.LogInformation("Infrastructure command consumer QoS configuration complete. Queue={QueueName} PrefetchCount=1", QueueName);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.RegisteredAsync += (_, args) =>
            {
                logger.LogInformation("Infrastructure command consumer registration confirmed. Queue={QueueName} ConsumerTags={ConsumerTags}",
                    QueueName, string.Join(',', args.ConsumerTags));
                return Task.CompletedTask;
            };
            consumer.ShutdownAsync += (_, reason) => SignalShutdownAsync("consumer", reason);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                try
                {
                    var result = await HandleAsync(delivery.Body, cancellationToken);
                    var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(delivery.Body.Span, JsonOptions)!;
                    await results.SubmitAsync(command, result.Execution, result.Result, cancellationToken);
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                }
                catch (UnauthorizedAccessException exception) when (IsWrongAddress(exception))
                {
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                }
                catch (Exception exception)
                {
                    // Leave the delivery unacknowledged; the attempt cleanup closes the channel and RabbitMQ redelivers it.
                    logger.LogError(exception, "Infrastructure command callback failed; stopping the consumer attempt with the delivery unacknowledged.");
                    stopped.TrySetResult(new InvalidOperationException(
                        "Infrastructure command callback failed; the delivery was left unacknowledged.", exception));
                }
            };

            logger.LogInformation("Infrastructure command consumer registration starting. Queue={QueueName}", QueueName);
            var consumerTag = await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, setupToken);
            logger.LogInformation("Infrastructure command consumer registration succeeded. Queue={QueueName} ConsumerTag={ConsumerTag}",
                QueueName, consumerTag);
            status.Set(InfrastructureCommandConsumerReadiness.Ready, $"ConsumerTag={consumerTag}");
            logger.LogInformation("Infrastructure command consumer READY. Queue={QueueName} ConsumerTag={ConsumerTag}",
                QueueName, consumerTag);

            var completed = await Task.WhenAny(
                stopped.Task,
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
            if (completed == stopped.Task && await stopped.Task is { } failure)
                throw failure;
        }
        finally
        {
            status.Set(InfrastructureCommandConsumerReadiness.NotReady, "Consumer attempt ended.");
            logger.LogInformation("Infrastructure command consumer cleanup starting. Queue={QueueName}", QueueName);
            await AbortAsync(channel, "channel");
            await AbortAsync(connection, "connection");
            logger.LogInformation("Infrastructure command consumer cleanup completed. Queue={QueueName}", QueueName);
        }
    }

    private async Task AbortAsync(IAsyncDisposable? resource, string resourceName)
    {
        if (resource is null)
            return;

        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            switch (resource)
            {
                case IChannel channel:
                    await channel.AbortAsync(cleanup.Token);
                    break;
                case IConnection connection:
                    await connection.AbortAsync(cleanup.Token);
                    break;
                default:
                    await resource.DisposeAsync().AsTask().WaitAsync(cleanup.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            logger.LogWarning("Infrastructure command consumer {Resource} cleanup timed out after {TimeoutSeconds} seconds.",
                resourceName, CleanupTimeout.TotalSeconds);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Infrastructure command consumer {Resource} cleanup failed.", resourceName);
        }
    }

    private static string Require(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]!
            : throw new InvalidOperationException($"{key} is required for the Infrastructure Runner consumer.");

    private static bool IsWrongAddress(UnauthorizedAccessException exception) =>
        exception.Message.Contains("does not match this Runner", StringComparison.Ordinal);

    private sealed class InfrastructureCommandConsumerStoppedException(string message) : Exception(message);

    private sealed class MissingInfrastructureCommandResultClient : IInfrastructureCommandResultClient
    {
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Infrastructure command result delivery is required before acknowledgement.");
    }
}
