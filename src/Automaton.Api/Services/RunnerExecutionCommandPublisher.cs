using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Automaton.Api.Services;

public sealed class RunnerExecutionCommandPublisher(IOptions<RabbitMqOptions> options)
    : IRuntimeTunnelReconnectCommandPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(RunnerExecutionCommand command, CancellationToken cancellationToken, bool management = false)
    {
        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = false
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = command.OperationId.ToString("N"),
            CorrelationId = command.OperationId.ToString("N"),
            Type = nameof(RunnerExecutionCommand)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions);
        var queueName = management ? ManagementQueueName(command.RunnerId) : QueueName(command.RunnerId);
        await channel.BasicPublishAsync("", queueName, mandatory: true, properties, payload, cancellationToken);
    }

    /// <summary>
    /// Tells a Runner that a host-container result is saved. Only the operation ID travels; the Runner reads the
    /// result over HTTPS. Not mandatory and short-lived: a missing or unread queue (an older Runner) loses nothing,
    /// because the Runner's fallback read and the database row still hold the result.
    /// </summary>
    public async Task PublishResultReadyAsync(Guid runnerId, Guid operationId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = false
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = operationId.ToString("N"),
            CorrelationId = operationId.ToString("N"),
            Type = nameof(RunnerExecutionResultReady)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RunnerExecutionResultReady(operationId, runnerId), JsonOptions);
        await channel.BasicPublishAsync("", ResultQueueName(runnerId), mandatory: false, properties, payload, cancellationToken);
    }

    public async Task<int> PublishRuntimeTunnelReconnectsAsync(
        IReadOnlyCollection<Guid> runnerIds,
        string? targetApiInstanceId,
        string? targetApiColor,
        CancellationToken cancellationToken)
    {
        if (runnerIds.Count == 0)
            return 0;

        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = false
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        foreach (var runnerId in runnerIds)
        {
            var command = new RunnerExecutionCommand(
                Guid.NewGuid(), runnerId, RunnerExecutionCommandKinds.ReconnectRuntimeTunnels,
                targetApiInstanceId, targetApiColor);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = command.OperationId.ToString("N"),
                CorrelationId = command.OperationId.ToString("N"),
                Type = nameof(RunnerExecutionCommand)
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions);
            await channel.BasicPublishAsync("", ManagementQueueName(runnerId), mandatory: true,
                properties, payload, cancellationToken);
        }

        return runnerIds.Count;
    }

    public static string QueueName(Guid runnerId) => $"automaton.runner-execution.{runnerId:N}";
    public static string ResultQueueName(Guid runnerId) => $"automaton.runner-result.{runnerId:N}";
    public static string ManagementQueueName(Guid runnerId) => $"automaton.runner-management.{runnerId:N}";
}
