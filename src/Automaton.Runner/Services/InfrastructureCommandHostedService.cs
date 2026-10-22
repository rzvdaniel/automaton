using Automaton.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>Runs the durable infrastructure command consumer for enrolled infrastructure runners.</summary>
public sealed class InfrastructureCommandHostedService(
    FileStorageService storage,
    IConfiguration configuration,
    AppActionDeploymentService deployment,
    AppActionExecutionService execution,
    ILogger<InfrastructureCommandHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var profile = await storage.LoadInstallationProfileAsync();
        var purpose = RunnerPurposes.Normalize(profile?.Purpose ?? Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        var hostId = profile?.RunnerHostId ?? (Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_HOST_ID"), out var parsedHostId) ? parsedHostId : Guid.Empty);
        if (!ShouldStart(purpose, storage.GetRunnerId(), hostId))
            return;
        var missing = await GetMissingConfigurationAsync();
        if (missing.Count > 0)
        {
            logger.LogWarning("Infrastructure command consumer disabled: incomplete RabbitMQ/API configuration ({MissingConfiguration}).", string.Join(", ", missing));
            return;
        }

        var identity = await InfrastructureCommandIdentity.LoadAsync(storage);
        var api = new InfrastructureCommandApiClient(configuration, storage);
        var executor = new TrustedInfrastructureActionExecutor(
            deployment, execution);
        var consumer = new InfrastructureCommandConsumer(identity, executor, api);
        logger.LogInformation("Starting durable infrastructure command consumer for Runner Host {RunnerHostId}", identity.RunnerHostId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.ConsumeAsync(configuration, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Infrastructure command consumer stopped; retrying with RabbitMQ recovery.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<List<string>> GetMissingConfigurationAsync()
    {
        var missing = new List<string>();
        AddMissingSetting(missing, "RabbitMq:Host", configuration["RabbitMq:Host"]);
        AddMissingSetting(missing, "RabbitMq:Username", configuration["RabbitMq:Username"]);
        AddMissingSetting(missing, "RabbitMq:Password", configuration["RabbitMq:Password"]);
        AddMissingSetting(missing, "API key", await storage.LoadApiKeyAsync());
        var serverUrl = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
            ?? await storage.LoadServerUrlAsync()
            ?? configuration["AppConfig:BaseUrl"];
        AddMissingSetting(missing, "API URL", serverUrl);
        return missing;
    }

    private static void AddMissingSetting(List<string> missing, string settingName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) missing.Add(settingName);
    }

    internal static bool ShouldStart(string? purpose, string? runnerId, Guid hostId)
        => RunnerPurposes.Normalize(purpose) == RunnerPurposes.Infrastructure
            && Guid.TryParse(runnerId, out var parsedRunnerId) && parsedRunnerId != Guid.Empty
            && hostId != Guid.Empty;
}
