using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Security;

/// <summary>
/// Counts rejected API keys per client address in a fixed window. Once an address has used up its allowance, further
/// requests that carry an API key are answered with 429 before the key is looked up (see <see cref="ApiKeyFailureMiddleware"/>).
/// </summary>
public sealed class ApiKeyFailureTracker(IMemoryCache cache, IOptions<RateLimitingOptions> options)
{
    /// <summary>Response header Traefik's access log keeps, so fail2ban can see a rejected key (value <see cref="HeaderValue"/>).</summary>
    public const string HeaderName = "X-Automaton-Auth-Failure";
    public const string HeaderValue = "api-key";

    private sealed class Counter { public int Count; }

    private RateLimitPolicyOptions Policy => options.Value.ApiKeyFailures;

    private static string CacheKey(string? address) => "api-key-failures:" + (address ?? "unknown");

    public bool IsBlocked(string? address)
        => cache.TryGetValue(CacheKey(address), out Counter? counter) && Volatile.Read(ref counter!.Count) >= Policy.PermitLimit;

    public void RecordFailure(string? address)
    {
        var counter = cache.GetOrCreate(CacheKey(address), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Policy.WindowSeconds);
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Count);
    }
}
