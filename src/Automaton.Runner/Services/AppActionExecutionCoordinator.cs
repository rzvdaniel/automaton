using System.Collections.Concurrent;

namespace Automaton.Runner.Console.Services;

/// <summary>Coordinates Action activation/pruning with executions using the same runtime directory.</summary>
public sealed class AppActionExecutionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(Guid appId, string actionKey, CancellationToken cancellationToken)
    {
        var gate = locks.GetOrAdd($"{appId:N}/{actionKey}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
