using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Node.Preparation;

internal sealed class NodePreparationService(
    NodePreparationState state, ILogger<NodePreparationService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state.TryStart())
            logger.LogInformation("Node preparation running; unenrolled and workload admission disabled.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (state.TryStop())
            logger.LogInformation("Node preparation stopped.");
        return Task.CompletedTask;
    }
}
