using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Core.Enums;
using Automaton.Infrastructure.RunnerHosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Resolves the privileged broker material used only while bootstrapping an
/// Infrastructure Runner. It is intentionally not part of any normal Runner
/// or Hosted Runner DTO.
/// </summary>
public sealed class InfrastructureRunnerRabbitMqConfigurationService(
    ApiKeyService apiKeyService,
    ApplicationDbContext db,
    IOptions<RabbitMqOptions> options,
    RunnerEnrollmentService runnerEnrollmentService)
{
    public async Task<string?> ResolveEnvironmentAsync(
        string? apiKey,
        string? enrollmentToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var key = await apiKeyService.ValidateApiKeyAsync(apiKey);
            if (key != null && key.Scope is (ApiKeyScope.InfrastructureRunnerEnroll or ApiKeyScope.Runner))
            {
                if (key.Scope != ApiKeyScope.Runner || await db.Runners.AnyAsync(
                        runner => runner.ApiKeyId == key.Id
                            && runner.Purpose == RunnerPurposes.Infrastructure,
                        cancellationToken))
                {
                    return TryBuildEnvironment(options.Value, out var environment)
                        ? environment
                        : null;
                }
            }
        }

        if (!await runnerEnrollmentService.IsInfrastructureEnrollmentTokenValidAsync(
                enrollmentToken ?? string.Empty,
                cancellationToken))
        {
            return null;
        }

        return TryBuildEnvironment(options.Value, out var enrollmentEnvironment)
            ? enrollmentEnvironment
            : null;
    }

    internal static bool TryBuildEnvironment(RabbitMqOptions rabbitMq, out string environment)
    {
        if (string.IsNullOrWhiteSpace(rabbitMq.Host)
            || string.IsNullOrWhiteSpace(rabbitMq.Username)
            || string.IsNullOrWhiteSpace(rabbitMq.Password)
            || rabbitMq.Port <= 0
            || ContainsLineBreak(rabbitMq.Host)
            || ContainsLineBreak(rabbitMq.Username)
            || ContainsLineBreak(rabbitMq.Password)
            || ContainsLineBreak(rabbitMq.VirtualHost)
            || ContainsLineBreak(rabbitMq.ExchangeName))
        {
            environment = string.Empty;
            return false;
        }

        environment = new RunnerRabbitMqConfiguration(
            rabbitMq.Host,
            rabbitMq.Port,
            rabbitMq.Username,
            rabbitMq.Password,
            rabbitMq.VirtualHost,
            rabbitMq.ExchangeName).ToEnvironmentFile();
        return true;
    }

    private static bool ContainsLineBreak(string value)
        => value.Contains('\r') || value.Contains('\n');
}
