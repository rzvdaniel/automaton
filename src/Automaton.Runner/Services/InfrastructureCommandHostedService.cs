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
    InfrastructureCommandConsumerStatus consumerStatus,
    ILogger<InfrastructureCommandHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var profile = await storage.LoadInstallationProfileAsync();
        var purpose = RunnerPurposes.Normalize(profile?.Purpose ?? Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        var hostId = profile?.RunnerHostId ?? (Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_HOST_ID"), out var parsedHostId) ? parsedHostId : Guid.Empty);
        if (!ShouldStart(purpose, storage.GetRunnerId(), hostId))
            return;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration["RabbitMq:Host"])) missing.Add("RabbitMq:Host");
        if (string.IsNullOrWhiteSpace(configuration["RabbitMq:Username"])) missing.Add("RabbitMq:Username");
        if (string.IsNullOrWhiteSpace(configuration["RabbitMq:Password"])) missing.Add("RabbitMq:Password");
        if (string.IsNullOrWhiteSpace(await storage.LoadApiKeyAsync())) missing.Add("API key");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL")
                ?? await storage.LoadServerUrlAsync()
                ?? configuration["AppConfig:BaseUrl"])) missing.Add("API URL");
        if (missing.Count > 0)
        {
            logger.LogWarning("Infrastructure command consumer disabled: incomplete RabbitMQ/API configuration ({MissingConfiguration}).", string.Join(", ", missing));
            return;
        }

        var identity = await InfrastructureCommandIdentity.LoadAsync(storage);
        var api = new InfrastructureCommandApiClient(configuration, storage);
        var executor = new TrustedInfrastructureActionExecutor(
            deployment, execution, api);
        var consumer = new InfrastructureCommandConsumer(identity, executor, api, consumerStatus, logger);
        logger.LogInformation("Starting durable infrastructure command consumer for Runner Host {RunnerHostId}", identity.RunnerHostId);
        await new InfrastructureCommandConsumerSupervisor(
            token => consumer.ConsumeAsync(configuration, token),
            TimeSpan.FromSeconds(5),
            logger).RunAsync(stoppingToken);
    }

    internal static bool ShouldStart(string? purpose, string? runnerId, Guid hostId)
        => RunnerPurposes.Normalize(purpose) == RunnerPurposes.Infrastructure
            && Guid.TryParse(runnerId, out var parsedRunnerId) && parsedRunnerId != Guid.Empty
            && hostId != Guid.Empty;
}
