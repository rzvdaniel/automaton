using Automaton.Core.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

internal sealed class HostedRunnerDeploymentCommandHostedService(
    FileStorageService storage,
    HostedRunnerDeploymentCommandQueueConsumer queueConsumer,
    ILogger<HostedRunnerDeploymentCommandHostedService> logger) : BackgroundService
{
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
                await queueConsumer.ConsumeAsync(broker, api, runnerId, stoppingToken);
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

    private async Task<Uri?> ResolveApiUrlAsync()
    {
        var value = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
            ?? await storage.LoadServerUrlAsync();
        return Uri.TryCreate(value, UriKind.Absolute, out var result) ? result : null;
    }

}
