using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Node.Preparation;

internal sealed class NodePreparationService(
    NodePreparationState state,
    INodeInstallationLock installationLock,
    ILogger<NodePreparationService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state.Phase != NodePreparationPhase.Created)
            return Task.CompletedTask;
        installationLock.Acquire(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryRequireRecovery())
                throw new InvalidOperationException("Node startup state is not eligible for local recovery.");
            logger.LogInformation("Node installation lock held; local authority unavailable; RecoveryRequired; admission disabled.");
            return Task.CompletedTask;
        }
        catch
        {
            installationLock.Release();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var stopped = state.TryStop();
        installationLock.Release();
        if (stopped)
            logger.LogInformation("Node preparation stopped; installation lock released.");
        return Task.CompletedTask;
    }
}
