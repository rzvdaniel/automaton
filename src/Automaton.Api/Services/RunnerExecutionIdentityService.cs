using Automaton.Api.Data;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Preserves enrolled-key binding and existing owner-key BYO registration authorization.</summary>
public sealed class RunnerExecutionIdentityService(ApplicationDbContext db, ResourceAuthorizationService authorization)
{
    public async Task EnsureAsync(Guid keyId, Guid runnerId, Guid authenticatedUserId, CancellationToken token)
    {
        if (keyId == Guid.Empty || runnerId == Guid.Empty)
            throw new UnauthorizedAccessException("Runner API-key authentication and identity are required.");
        var runner = await db.Runners.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runnerId, token)
            ?? throw new UnauthorizedAccessException("Runner was not found.");
        if (runner.ApiKeyId.HasValue)
        {
            if (runner.ApiKeyId == keyId) return;
            throw new UnauthorizedAccessException("The API key belongs to a different Runner.");
        }
        if (authenticatedUserId == Guid.Empty
            || !await authorization.CanUpdateRunnerAsync(runnerId, authenticatedUserId, token))
            throw new UnauthorizedAccessException("The owner API key cannot manage this BYO Runner.");
        if (RunnerCapabilities.RequiresAdministrator(runner.CapabilitiesJson, runner.Purpose)
            && !await RunnerExecutionAdmissionService.IsAdministratorAsync(db, authenticatedUserId, token))
            throw new UnauthorizedAccessException("Privileged Runner execution requires an administrator.");
    }
}
