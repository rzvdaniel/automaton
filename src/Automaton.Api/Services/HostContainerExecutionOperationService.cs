using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostContainerExecutionOperationService(
    ApplicationDbContext db,
    RunnerExecutionOperationService runnerOperations)
{
    public async Task<HostContainerExecutionOperationResponse?> GetResultAsync(
        Guid apiKeyId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var runner = await GetRunnerForApiKeyAsync(apiKeyId, RunnerPurposes.Managed, cancellationToken);
        if (runner is null)
            return null;
        if (RunnerApplicationProfiles.TryNormalize(runner.ApplicationProfile, out var profile)
            && profile == RunnerApplicationProfiles.MachineManagement)
        {
            if (!await db.RunnerExecutionOperations.AsNoTracking()
                    .AnyAsync(item => item.Id == operationId && item.RunnerId == runner.Id, cancellationToken))
                return null;
            return await runnerOperations.ReadHostContainerOperationAsync(
                apiKeyId, operationId, Guid.Empty, runner.Id, cancellationToken);
        }
        return null;
    }

    private Task<Runner?> GetRunnerForApiKeyAsync(
        Guid apiKeyId,
        string purpose,
        CancellationToken cancellationToken)
        => db.Runners.SingleOrDefaultAsync(runner => runner.ApiKeyId == apiKeyId
            && runner.Purpose == purpose, cancellationToken);

}
