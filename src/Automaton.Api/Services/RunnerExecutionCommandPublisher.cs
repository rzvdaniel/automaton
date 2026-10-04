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
    public static string ManagementQueueName(Guid runnerId) => $"automaton.runner-management.{runnerId:N}";
}
