using System.Collections.Concurrent;

namespace Automaton.Api.Services;

/// <summary>Bounds streaming responses and WebSockets per app so one app cannot hold every slot of a Runner.</summary>
public sealed class TunnelLongLivedLimiter
{
    private readonly ConcurrentDictionary<Guid, int> counts = new();

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
