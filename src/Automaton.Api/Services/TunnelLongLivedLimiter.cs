using System.Collections.Concurrent;

namespace Automaton.Api.Services;

/// <summary>Bounds streaming responses and WebSockets per app so one app cannot hold every slot of a Runner.</summary>
public sealed class TunnelLongLivedLimiter
{
    private readonly ConcurrentDictionary<Guid, int> counts = new();
    private readonly ConcurrentDictionary<Guid, int> waiting = new();

    public int Count(Guid appId) => counts.TryGetValue(appId, out var count) ? count : 0;

    public IDisposable? TryAcquire(Guid appId, int limit)
    {
        var count = counts.AddOrUpdate(appId, 1, (_, current) => current + 1);
        if (count > limit)
        {
            Release(appId);
            return null;
        }
        return new Lease(this, appId);
    }

    /// <summary>Like <see cref="TryAcquire"/>, but waits up to <paramref name="wait"/> for a slot, so a burst of large files queues instead of failing.</summary>
    public async Task<IDisposable?> TryAcquireAsync(Guid appId, int limit, int maxWaiting, TimeSpan wait, CancellationToken cancellationToken)
    {
        var lease = TryAcquire(appId, limit);
        if (lease is not null || wait <= TimeSpan.Zero) return lease;

        // Only so many may queue per app; the rest fail at once, so waiting cannot be used to pile up work.
        if (waiting.AddOrUpdate(appId, 1, (_, current) => current + 1) > maxWaiting)
        {
            EndWait(appId);
            return null;
        }

        try
        {
            var deadline = DateTime.UtcNow + wait;
            while (true)
            {
                lease = TryAcquire(appId, limit);
                if (lease is not null || DateTime.UtcNow >= deadline) return lease;
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }
        }
        finally
        {
            EndWait(appId);
        }
    }

    private void EndWait(Guid appId)
        => waiting.AddOrUpdate(appId, 0, (_, current) => Math.Max(0, current - 1));

    private void Release(Guid appId)
        => counts.AddOrUpdate(appId, 0, (_, current) => Math.Max(0, current - 1));

    private sealed class Lease(TunnelLongLivedLimiter owner, Guid appId) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                owner.Release(appId);
        }
    }
}
