using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostedRunnerStorageActionDeployment(
    ApplicationDbContext db,
    ITrustedInfrastructureActionDeploymentService trustedActionDeployment)
{
    public async Task<bool> TryEnsureDeployedAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var runner = await db.HostedRunners
            .Include(x => x.RunnerHost)
            .ThenInclude(x => x!.InfrastructureRunner)
            .SingleOrDefaultAsync(x => x.Id == hostedRunnerId && x.DeletedAt == null, cancellationToken);
        var infrastructureRunner = runner?.RunnerHost?.InfrastructureRunner;
        if (infrastructureRunner?.Purpose != RunnerPurposes.Infrastructure
            || !RunnerPresenceEvaluator.IsOnline(
                infrastructureRunner.ConnectionId, infrastructureRunner.LastHeartbeatAtUtc, DateTime.UtcNow))
            return false;

        await trustedActionDeployment.EnsureDeployedAsync(
            infrastructureRunner.ConnectionId!, StorageTelemetryActions.MeasureHostedRunnerStorage, cancellationToken);
        return true;
    }
}
