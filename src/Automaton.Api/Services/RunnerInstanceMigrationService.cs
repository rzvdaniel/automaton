using Automaton.Api.Data;
using Automaton.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Coordinates Runner reconnect requests and reports drain and migration state for this API instance.
/// </summary>
public sealed class RunnerInstanceMigrationService(
    ApplicationDbContext dbContext,
    ApiInstanceIdentityService apiInstanceIdentityService,
    IHubContext<AutomatonHub> automatonHub)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<RunnerInstanceMigrationService>();

    public async Task<ReconnectWhenIdleResult> RequestReconnectWhenIdleAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        var connectedRunners = await dbContext.Runners
            .AsNoTracking()
            .Where(runner => runner.ConnectionId != null)
            .Where(runner => runner.ApiInstanceId == apiInstanceIdentityService.ApiInstanceId)
            .Select(runner => new { runner.Id, runner.Name, runner.ConnectionId })
            .ToListAsync(cancellationToken);

        var signaled = 0;
        foreach (var runner in connectedRunners)
        {
            try
            {
                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync("ReconnectWhenIdle", reason, cancellationToken);
                signaled++;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to request reconnect-when-idle for runner {RunnerId} ({RunnerName})", runner.Id, runner.Name);
            }
        }

        logger.Debug(
            "Requested reconnect-when-idle for {RunnerCount} connected runners owned by API instance {ApiInstanceId} color {ApiColor}",
            signaled,
            apiInstanceIdentityService.ApiInstanceId,
            apiInstanceIdentityService.ApiColor ?? "(unset)");
        return new ReconnectWhenIdleResult
        {
            CandidateRunnerIds = connectedRunners.Select(runner => runner.Id).ToArray(),
            SignaledRunnerCount = signaled
        };
    }

    public async Task<RunnerDrainStatus> GetDrainStatusAsync(CancellationToken cancellationToken)
    {
        var connectedRunnerIds = await dbContext.Runners
            .AsNoTracking()
            .Where(runner => runner.ConnectionId != null &&
                runner.ApiInstanceId == apiInstanceIdentityService.ApiInstanceId)
            .Select(runner => runner.Id)
            .ToArrayAsync(cancellationToken);
        return new RunnerDrainStatus
        {
            ConnectedRunnerIds = connectedRunnerIds,
            ConnectedRunnerCount = connectedRunnerIds.Length,
            Drained = connectedRunnerIds.Length == 0
        };
    }

    public async Task<RunnerMigrationStatus> GetMigrationStatusAsync(
        IReadOnlyCollection<Guid> runnerIds,
        CancellationToken cancellationToken)
    {
        var connectedRunnerIds = await dbContext.Runners
            .AsNoTracking()
            .Where(runner => runnerIds.Contains(runner.Id)
                && runner.ConnectionId != null
                && runner.ApiInstanceId == apiInstanceIdentityService.ApiInstanceId)
            .Select(runner => runner.Id)
            .ToArrayAsync(cancellationToken);
        return new RunnerMigrationStatus
        {
            ExpectedRunnerIds = runnerIds.ToArray(),
            ConnectedRunnerIds = connectedRunnerIds,
            AllConnected = connectedRunnerIds.Length == runnerIds.Count
        };
    }

    public async Task<RunnerMigrationStatus> GetMigrationCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var candidateRunnerIds = await dbContext.Runners
            .AsNoTracking()
            .Where(runner => runner.ConnectionId != null &&
                runner.ApiInstanceId != apiInstanceIdentityService.ApiInstanceId)
            .Select(runner => runner.Id)
            .ToArrayAsync(cancellationToken);
        return new RunnerMigrationStatus { ExpectedRunnerIds = candidateRunnerIds };
    }
}
