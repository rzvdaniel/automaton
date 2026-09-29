using Automaton.Api.Data;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostedRunnerBrokerConfigurationService(
    ApiKeyService apiKeyService,
    ApplicationDbContext db,
    HostedRunnerBrokerProvisioner provisioner)
{
    public async Task<HostedRunnerBrokerConfiguration?> ProvisionAsync(
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var key = await apiKeyService.ValidateApiKeyAsync(apiKey);
        if (key is null || key.Scope != ApiKeyScope.Runner)
            return null;

        var runner = await db.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ApiKeyId == key.Id
                && item.Purpose == RunnerPurposes.Managed, cancellationToken);
        return runner is null
            ? null
            : await provisioner.ProvisionAsync(runner.Id, cancellationToken);
    }
}
