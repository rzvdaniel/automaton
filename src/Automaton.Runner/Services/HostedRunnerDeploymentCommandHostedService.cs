using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentCommandHostedService(
    FileStorageService storage,
    HostedRunnerDeploymentCommandProcessor processor,
    ILogger<HostedRunnerDeploymentCommandHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"))
            != RunnerPurposes.Managed)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var apiKey = await storage.LoadApiKeyAsync();
                var apiUrl = await ResolveApiUrlAsync();
                if (string.IsNullOrWhiteSpace(apiKey) || apiUrl is null
                    || !Guid.TryParse(storage.GetRunnerId(), out var runnerId))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
                var api = new HostedRunnerCommandApiClient(apiUrl, apiKey);
                var broker = await api.LoadBrokerConfigurationAsync(stoppingToken);
                await ConsumeAsync(broker, api, runnerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Hosted Runner deployment consumer stopped; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(
        HostedRunnerBrokerConfiguration broker,
        HostedRunnerCommandApiClient api,
        Guid runnerId,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = broker.Host,
            Port = broker.Port,
            UserName = broker.Username,
            Password = broker.Password,
            VirtualHost = broker.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var command = Deserialize(delivery.Body);
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
                {
                    processor.DeleteJournal(command);
                    processor.DeleteExecutionResult(command);
                }
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsPermanentlyInvalid(exception))
            {
                logger.LogError(exception, "Discarding permanently invalid Hosted Runner deployment command.");
                // The queue has no dead-letter exchange, so a non-requeued reject is terminal.
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Hosted Runner deployment command failed and will be retried.");
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        };
        await channel.BasicConsumeAsync(broker.QueueName, autoAck: false, consumer, cancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task<Uri?> ResolveApiUrlAsync()
    {
        var value = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
            ?? await storage.LoadServerUrlAsync();
        return Uri.TryCreate(value, UriKind.Absolute, out var result) ? result : null;
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
