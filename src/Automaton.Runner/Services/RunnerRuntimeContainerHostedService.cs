using Microsoft.Extensions.Hosting;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeContainerHostedService(RunnerRuntimeContainerManager runtimeManager)
    : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
        => runtimeManager.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => runtimeManager.StopAsync(cancellationToken);

    public void Dispose()
        => runtimeManager.Dispose();
}
