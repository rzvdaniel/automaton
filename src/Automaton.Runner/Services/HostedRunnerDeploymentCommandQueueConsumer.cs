using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentCommandQueueConsumer(
    HostedRunnerDeploymentCommandProcessor processor,
    HostedRunnerInvalidCommandRecovery invalidCommandRecovery,
    ILogger<HostedRunnerDeploymentCommandQueueConsumer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ConsumeAsync(
        HostedRunnerBrokerConfiguration broker,
        HostedRunnerCommandApiClient api,
        Guid runnerId,
        CancellationToken cancellationToken)
    {
        var factory = RunnerBrokerConnectionFactory.Create(broker);
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(
            channel, delivery, api, runnerId, cancellationToken);
        await channel.BasicConsumeAsync(broker.QueueName, autoAck: false, consumer, cancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        HostedRunnerCommandApiClient api,
        Guid runnerId,
        CancellationToken cancellationToken)
    {
        HostedRunnerDeploymentCommand? command = null;
        try
        {
            command = Deserialize(delivery.Body);
            EnsureAddressedToRunner(command, runnerId);
            var disposition = await api.GetCommandDispositionAsync(command, cancellationToken);
            var removeOperationResult = disposition is "already-applied" or "obsolete";
            if (disposition == "execute")
            {
                var result = await processor.ProcessAsync(command, api, cancellationToken);
                var outcome = await api.SubmitResultAsync(command, result, cancellationToken);
                removeOperationResult = outcome is "accepted" or "already-applied" or "obsolete";
            }
            else if (disposition is not ("already-applied" or "stale" or "obsolete"))
            {
                throw new InvalidDataException("The API returned an unknown command disposition.");
            }
            if (removeOperationResult)
                RemoveOperationResult(command);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsPermanentlyInvalid(exception))
        {
            await RejectInvalidCommandAsync(channel, delivery, command, api, exception, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RetryDeliveryAsync(channel, delivery, exception, cancellationToken);
        }
    }

    private async Task RejectInvalidCommandAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        HostedRunnerDeploymentCommand? command,
        HostedRunnerCommandApiClient api,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var identity = HostedRunnerInvalidCommandRecovery.GetCommandIdentity(
            delivery.BasicProperties.MessageId, delivery.BasicProperties.CorrelationId, command);
        await invalidCommandRecovery.RejectAsync(identity.OperationId, identity.AttemptId,
            delivery.DeliveryTag, exception, api.RejectCommandAsync,
            (requeue, token) => channel.BasicRejectAsync(delivery.DeliveryTag, requeue, token).AsTask(),
            cancellationToken);
    }

    private async Task RetryDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Hosted Runner deployment command failed and will be retried.");
        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private void RemoveOperationResult(HostedRunnerDeploymentCommand command)
    {
        processor.DeleteJournal(command);
        processor.DeleteExecutionResult(command);
    }

    private static HostedRunnerDeploymentCommand Deserialize(ReadOnlyMemory<byte> body)
        => JsonSerializer.Deserialize<HostedRunnerDeploymentCommand>(body.Span, JsonOptions)
            ?? throw new InvalidDataException("The Hosted Runner deployment command is empty.");

    private static void EnsureAddressedToRunner(HostedRunnerDeploymentCommand command, Guid runnerId)
    {
        if (command.RunnerId != runnerId || command.OperationId == Guid.Empty
            || command.ExecutionAttemptId == Guid.Empty || command.AppId == Guid.Empty
            || command.DeploymentTargetId == Guid.Empty || command.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("The deployment command identity or timestamp is invalid.");
    }

    private static bool IsPermanentlyInvalid(Exception exception)
        => exception is JsonException or InvalidDataException;
}
