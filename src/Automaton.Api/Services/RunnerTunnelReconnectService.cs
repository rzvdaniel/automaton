using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerTunnelReconnectService(
    ApplicationDbContext dbContext,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IRuntimeTunnelReconnectCommandPublisher commandPublisher,
    ILogger<RunnerTunnelReconnectService> logger)
{
    public async Task<RunnerTunnelReconnectResult> RequestForApiColorAsync(
        string targetApiColor,
        bool reconcileAllRegisteredRunners,
        CancellationToken cancellationToken)
    {
        var candidates = dbContext.Runners
            .AsNoTracking();
        if (!reconcileAllRegisteredRunners)
            candidates = candidates.Where(runner => runner.ConnectionId != null && runner.ApiColor != targetApiColor);

        var connectedRunners = await candidates
            .Select(runner => new { runner.Id, runner.ApiCompatibilityVersion })
            .ToListAsync(cancellationToken);

        var incompatibleRunners = connectedRunners
            .Where(runner => !RunnerProtocolCompatibilityPolicy.SupportsRuntimeTunnelReconnectCommands(
                runner.ApiCompatibilityVersion))
            .ToArray();
        if (incompatibleRunners.Length > 0 && !reconcileAllRegisteredRunners)
        {
            foreach (var runner in incompatibleRunners)
            {
                logger.LogWarning(
                    "Runner {RunnerId} cannot accept the API cutover tunnel reconnect command; upgrade its protocol first",
                    runner.Id);
            }

            return new RunnerTunnelReconnectResult(connectedRunners.Count, 0, incompatibleRunners.Length);
        }

        if (reconcileAllRegisteredRunners)
        {
            foreach (var runner in incompatibleRunners)
            {
                logger.LogInformation(
                    "Skipping protocol-incompatible Runner {RunnerId} during tunnel rollback reconciliation",
                    runner.Id);
            }
        }

        var supportedRunners = connectedRunners
            .Where(runner => RunnerProtocolCompatibilityPolicy.SupportsRuntimeTunnelReconnectCommands(
                runner.ApiCompatibilityVersion))
            .ToArray();
        try
        {
            var requested = await commandPublisher.PublishRuntimeTunnelReconnectsAsync(
                supportedRunners.Select(runner => runner.Id).ToArray(),
                targetApiColor == apiInstanceIdentityService.ApiColor
                    ? apiInstanceIdentityService.ApiInstanceId
                    : null,
                targetApiColor,
                cancellationToken);
            return new RunnerTunnelReconnectResult(
                connectedRunners.Count, requested, reconcileAllRegisteredRunners ? 0 : incompatibleRunners.Length);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Could not publish runtime tunnel reconnect commands for the previous API color");
            return new RunnerTunnelReconnectResult(connectedRunners.Count, 0, connectedRunners.Count);
        }
    }
}

public sealed record RunnerTunnelReconnectResult(int CandidateRunnerCount, int RequestedCount, int FailedCount);
