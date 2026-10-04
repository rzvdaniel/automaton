using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerTunnelReconnectService(
    ApplicationDbContext dbContext,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IRuntimeTunnelReconnectCommandPublisher commandPublisher,
    ILogger<RunnerTunnelReconnectService> logger)
{
    public async Task<RunnerTunnelReconnectResult> RequestForPreviousInstancesAsync(
        CancellationToken cancellationToken)
    {
        var candidates = dbContext.Runners
            .AsNoTracking()
            .Where(runner => runner.ConnectionId != null);
        candidates = apiInstanceIdentityService.ApiColor is { Length: > 0 } currentApiColor
            ? candidates.Where(runner => runner.ApiColor != currentApiColor)
            : candidates.Where(runner => runner.ApiInstanceId != apiInstanceIdentityService.ApiInstanceId);

        var connectedRunners = await candidates
            .Select(runner => new { runner.Id, runner.ApiCompatibilityVersion })
            .ToListAsync(cancellationToken);

        var incompatibleRunners = connectedRunners
            .Where(runner => !RunnerProtocolCompatibilityPolicy.SupportsRuntimeTunnelReconnectCommands(
                runner.ApiCompatibilityVersion))
            .ToArray();
        if (incompatibleRunners.Length > 0)
        {
            foreach (var runner in incompatibleRunners)
            {
                logger.LogWarning(
                    "Runner {RunnerId} cannot accept the API cutover tunnel reconnect command; upgrade its protocol first",
                    runner.Id);
            }

            return new RunnerTunnelReconnectResult(connectedRunners.Count, 0, incompatibleRunners.Length);
        }

        try
        {
            var requested = await commandPublisher.PublishRuntimeTunnelReconnectsAsync(
                connectedRunners.Select(runner => runner.Id).ToArray(),
                apiInstanceIdentityService.ApiInstanceId,
                apiInstanceIdentityService.ApiColor,
                cancellationToken);
            return new RunnerTunnelReconnectResult(connectedRunners.Count, requested, 0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Could not publish runtime tunnel reconnect commands for the previous API color");
            return new RunnerTunnelReconnectResult(connectedRunners.Count, 0, connectedRunners.Count);
        }
    }
}

public sealed record RunnerTunnelReconnectResult(int ConnectedRunnerCount, int RequestedCount, int FailedCount);
