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
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IOptions<InfrastructureRunnerRabbitMqOptions> infrastructureRunnerRabbitMqOptions,
    RunnerEnrollmentService runnerEnrollmentService)
{
    public async Task<string?> ResolveEnvironmentAsync(
        string? apiKey,
        string? enrollmentToken = null,
        CancellationToken cancellationToken = default)
    {
        var apiKeyResolution = await ResolveApiKeyEnvironmentAsync(apiKey, cancellationToken);
        if (apiKeyResolution.Accepted)
        {
            return apiKeyResolution.Environment;
        }

        if (!await runnerEnrollmentService.IsInfrastructureEnrollmentTokenValidAsync(
                enrollmentToken ?? string.Empty,
                cancellationToken))
        {
            return null;
        }

        return TryBuildEnvironment(
            rabbitMqOptions.Value,
            infrastructureRunnerRabbitMqOptions.Value,
            out var enrollmentEnvironment)
            ? enrollmentEnvironment
            : null;
    }

    private async Task<(bool Accepted, string? Environment)> ResolveApiKeyEnvironmentAsync(
        string? apiKey,
        CancellationToken cancellationToken)
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
                    var environment = TryBuildEnvironment(
                        rabbitMqOptions.Value,
                        infrastructureRunnerRabbitMqOptions.Value,
                        out var builtEnvironment)
                        ? builtEnvironment
                        : null;
                    return (true, environment);
                }
            }
        }

        return (false, null);
    }

    internal static bool TryBuildEnvironment(
        RabbitMqOptions rabbitMq,
        InfrastructureRunnerRabbitMqOptions infrastructureRunnerRabbitMq,
        out string environment)
    {
        if (string.IsNullOrWhiteSpace(infrastructureRunnerRabbitMq.Host)
            || string.IsNullOrWhiteSpace(rabbitMq.Username)
            || string.IsNullOrWhiteSpace(rabbitMq.Password)
            || infrastructureRunnerRabbitMq.Port <= 0
            || ContainsLineBreak(infrastructureRunnerRabbitMq.Host)
            || ContainsLineBreak(rabbitMq.Username)
            || ContainsLineBreak(rabbitMq.Password)
            || ContainsLineBreak(rabbitMq.VirtualHost)
            || ContainsLineBreak(rabbitMq.ExchangeName))
        {
            environment = string.Empty;
            return false;
        }

        environment = new RunnerRabbitMqConfiguration(
            infrastructureRunnerRabbitMq.Host,
            infrastructureRunnerRabbitMq.Port,
            rabbitMq.Username,
            rabbitMq.Password,
            rabbitMq.VirtualHost,
            rabbitMq.ExchangeName).ToEnvironmentFile();
        return true;
    }

    private static bool ContainsLineBreak(string value)
        => value.Contains('\r') || value.Contains('\n');
}
