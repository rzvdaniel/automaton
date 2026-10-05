using Automaton.Api.Data;
using Automaton.Core.Enums;
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
        // Recorded presence/color can lag an earlier queued cutover. Always append
        // the latest target to every compatible Runner's serial management queue.
        var registeredRunners = await dbContext.Runners.AsNoTracking()
            // Revocation retains Infrastructure Runner rows but disables their
            // credentials and removes their queues. Legacy BYO registrations
            // can still authenticate with an owner key without a bound key.
            .Where(runner => runner.ApiKeyId.HasValue
                ? runner.ApiKey != null && runner.ApiKey.IsActive
                : runner.Purpose != RunnerPurposes.Infrastructure)
            // Successful Hosted Runner deletion clears its credential/Runner links
            // and removes queues, while retaining the generic registration and key.
            // Source identifies managed credentials without excluding legacy BYO keys.
            .Where(runner => runner.ApiKey == null
                || runner.ApiKey.Source != ApiKeySource.RunnerProvisioning
                || dbContext.HostedRunners.Any(hosted => hosted.DeletedAt == null
                    && (hosted.ApiKeyId == runner.ApiKeyId || hosted.RunnerId == runner.Id)))
            .Select(runner => new { runner.Id, runner.ApiCompatibilityVersion, IsConnected = runner.ConnectionId != null })
            .ToListAsync(cancellationToken);

        var incompatibleRunners = registeredRunners
            .Where(runner => !RunnerProtocolCompatibilityPolicy.SupportsRuntimeTunnelReconnectCommands(
                runner.ApiCompatibilityVersion))
            .ToArray();
        var connectedIncompatibleCount = incompatibleRunners.Count(runner => runner.IsConnected);
        if (connectedIncompatibleCount > 0 && !reconcileAllRegisteredRunners)
        {
            foreach (var runner in incompatibleRunners.Where(runner => runner.IsConnected))
            {
                logger.LogWarning(
                    "Runner {RunnerId} cannot accept the API cutover tunnel reconnect command; upgrade its protocol first",
                    runner.Id);
            }

            return new RunnerTunnelReconnectResult(registeredRunners.Count, 0, connectedIncompatibleCount);
        }

        foreach (var runner in incompatibleRunners)
        {
            logger.LogInformation(
                "Skipping protocol-incompatible Runner {RunnerId} during tunnel reconciliation",
                runner.Id);
        }

        var supportedRunners = registeredRunners
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
                registeredRunners.Count, requested, 0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Could not publish runtime tunnel reconnect commands for the previous API color");
            return new RunnerTunnelReconnectResult(registeredRunners.Count, 0, supportedRunners.Length);
        }
    }
}

public sealed record RunnerTunnelReconnectResult(int CandidateRunnerCount, int RequestedCount, int FailedCount);
