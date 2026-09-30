namespace Automaton.Api.Services;

/// <summary>Closes unfinished workflow work and removes its broker access when a Runner is retired.</summary>
public sealed class RunnerExecutionRevocationService(
    RunnerExecutionOperationService operations, HostedRunnerBrokerProvisioner broker)
{
    public async Task RevokeAsync(Guid runnerId, CancellationToken token)
    {
        await operations.TerminateRunnerAsync(runnerId, token);
        await broker.RevokeExecutionAsync(runnerId, token);
    }
}
