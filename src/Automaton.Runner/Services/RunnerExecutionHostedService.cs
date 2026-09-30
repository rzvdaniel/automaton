using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionHostedService(FileStorageService storage,
    RunnerExecutionQueueConsumer consumer, ILogger<RunnerExecutionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = stoppingToken;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var key = await storage.LoadApiKeyAsync();
                var url = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL") ?? await storage.LoadServerUrlAsync();
                if (key is not null && Uri.TryCreate(url, UriKind.Absolute, out var address)
                    && Guid.TryParse(storage.GetRunnerId(), out var runnerId))
                {
                    using var api = new RunnerExecutionApiClient(address, key, runnerId);
                    var broker = await api.ConfigurationAsync(token);
                    await consumer.ConsumeAsync(broker, api, runnerId, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Runner execution consumer stopped; retrying existing commands.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }
}
