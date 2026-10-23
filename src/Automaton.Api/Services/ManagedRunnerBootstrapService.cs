using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerBootstrapService
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;

    public ManagedRunnerBootstrapService(
        ApplicationDbContext dbContext,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy)
    {
        this.dbContext = dbContext;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
    }

    public async Task<ManagedRunnerBootstrapResult> EnsureRunnerAssignmentAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetAssignedRunnerResultAsync(appId, ownerId, cancellationToken);
        }
        catch (AppDeploymentTargetUnassignedException)
        {
            return ManagedRunnerBootstrapResult.Unavailable(
                "Assign a Management Runner to this app before deploying.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ManagedRunnerBootstrapResult.Unavailable(ex.Message);
        }

    }

    private async Task<ManagedRunnerBootstrapResult> GetAssignedRunnerResultAsync(
        Guid appId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var assignedRunner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId, ownerId, requireOnline: false, cancellationToken: cancellationToken);
        if (await dbContext.HostedRunners.AnyAsync(
            item => item.RunnerId == assignedRunner.Id, cancellationToken))
            return ManagedRunnerBootstrapResult.Unavailable(
                "Per-user Hosted Runner execution is retired. Assign a Management Runner to this app before deploying.");

        return GetRunnerPresenceResult(assignedRunner);
    }

    private ManagedRunnerBootstrapResult GetRunnerPresenceResult(Runner runner)
        => RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow)
            ? ManagedRunnerBootstrapResult.Ready(runner.Id)
            : ManagedRunnerBootstrapResult.Unavailable("The assigned Runner is offline.");

}

public sealed class ManagedRunnerBootstrapResult
{
    public bool IsReady { get; init; }
    public Guid? RunnerId { get; init; }
    public string State { get; init; } = AppDeployStates.Ready;
    public string Message { get; init; } = string.Empty;
    public bool RequiresRetry { get; init; }

    public static ManagedRunnerBootstrapResult Ready(Guid runnerId) => new()
    {
        IsReady = true,
        RunnerId = runnerId,
        State = AppDeployStates.Ready
    };

    public static ManagedRunnerBootstrapResult Unavailable(string message) => new()
    {
        State = AppDeployStates.ManagedRuntimeUnavailable,
        Message = message
    };
}
