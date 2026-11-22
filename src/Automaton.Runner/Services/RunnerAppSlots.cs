using System.Collections.Concurrent;

namespace Automaton.Runner.Console.Services;

/// <summary>Bounds the requests, streams and WebSockets one app may have in flight on this Runner, so one busy app cannot take the whole Runner.</summary>
internal sealed class RunnerAppSlots(TunnelForwarderConfig config)
{
    private readonly ConcurrentDictionary<Guid, int> activePerApp = new();
    private readonly ConcurrentDictionary<Guid, int> waitingPerApp = new();

    /// <summary>Takes one of the app's slots, or null when the app already has its share in flight.</summary>
    public IDisposable? TryEnter(Guid? appId)
    {
        if (appId is not { } id) return NoLease.Instance;
        var count = activePerApp.AddOrUpdate(id, 1, (_, current) => current + 1);
        if (count > config.MaxConcurrentRequestsPerApp)
        {
            Release(id);
            return null;
        }
        return new Lease(this, id);
    }

    /// <summary>Like <see cref="TryEnter"/>, but a busy app's request waits a few seconds for a slot (up to a cap of waiters) before it is refused.</summary>
    public async Task<IDisposable?> TryEnterAsync(Guid? appId, CancellationToken cancellationToken)
    {
        var lease = TryEnter(appId);
        if (lease is not null || appId is not { } id || config.SlotWaitSeconds <= 0) return lease;

        if (waitingPerApp.AddOrUpdate(id, 1, (_, current) => current + 1) > config.MaxWaitingPerApp)
        {
            EndWait(id);
            return null;
        }

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(config.SlotWaitSeconds);
            while (true)
            {
                lease = TryEnter(id);
                if (lease is not null || DateTime.UtcNow >= deadline) return lease;
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }
        }
        finally
        {
            EndWait(id);
        }
    }

    private void EndWait(Guid appId)
        => waitingPerApp.AddOrUpdate(appId, 0, (_, current) => Math.Max(0, current - 1));

    private void Release(Guid appId)
        => activePerApp.AddOrUpdate(appId, 0, (_, current) => Math.Max(0, current - 1));

    private sealed class Lease(RunnerAppSlots owner, Guid appId) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) owner.Release(appId);
        }
    }

    private sealed class NoLease : IDisposable
    {
        public static readonly NoLease Instance = new();
        public void Dispose() { }
    }
}
