using System.Collections.Concurrent;

namespace Automaton.Api.Services;

/// <summary>
/// Remembers what a tunnel request looked up (target, Runner, hosted-Runner check) for a few seconds, so a page that
/// loads many files at once costs one database round trip instead of one connection per file. Requests that arrive
/// while the lookup runs wait for it. A failed lookup is never remembered.
/// </summary>
public sealed class RuntimeTunnelTargetCache
{
    internal static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<(Guid AppId, Guid TargetId), Entry> entries = new();
    private readonly TimeSpan timeToLive;
    private readonly TimeProvider time;

    public RuntimeTunnelTargetCache() : this(DefaultTimeToLive, TimeProvider.System) { }

    internal RuntimeTunnelTargetCache(TimeSpan timeToLive, TimeProvider time)
    {
        this.timeToLive = timeToLive;
        this.time = time;
    }

    public async Task<T> GetOrLoadAsync<T>(Guid appId, Guid targetId, Func<Task<T>> load) where T : class
    {
        var key = (appId, targetId);
        var now = time.GetUtcNow();
        if (entries.Count > 1024) Sweep(now);

        var entry = entries.AddOrUpdate(
            key,
            _ => new Entry(now + timeToLive, new Lazy<Task<object>>(() => Load(load))),
            (_, existing) => existing.ExpiresAt > now ? existing : new Entry(now + timeToLive, new Lazy<Task<object>>(() => Load(load))));
        try
        {
            return (T)await entry.Value.Value;
        }
        catch
        {
            entries.TryRemove(new KeyValuePair<(Guid, Guid), Entry>(key, entry));
            // The shared lookup failed (for example its request was cancelled): do this request's own lookup.
            return await load();
        }
    }

    private static async Task<object> Load<T>(Func<Task<T>> load) where T : class => await load();

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in entries)
            if (pair.Value.ExpiresAt <= now) entries.TryRemove(pair);
    }

    private sealed record Entry(DateTimeOffset ExpiresAt, Lazy<Task<object>> Value);
}
