using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Automaton.Api.Services;

public sealed class HostedRunnerDeploymentCommandPublisher(IOptions<RabbitMqOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(HostedRunnerDeploymentCommand command, CancellationToken cancellationToken)
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
            Type = nameof(HostedRunnerDeploymentCommand)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions);
        var queueName = QueueName(command.RunnerId);
        await channel.BasicPublishAsync("", queueName, mandatory: true, properties, payload, cancellationToken);
    }

    public static string QueueName(Guid runnerId) => $"automaton.hosted-runner.{runnerId:N}";
}
