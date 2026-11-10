using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Security;

/// <summary>
/// Remembers, for a few minutes, API keys that authentication has accepted, by hash. The rate limiter runs before
/// authentication, so this is how it tells a real Runner key (own, larger bucket) from any header value (normal bucket).
/// </summary>
internal static class RateLimitedApiKeys
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    internal static void Remember(IMemoryCache cache, string apiKey)
        => cache.Set(CacheKey(apiKey), true, Lifetime);

    /// <summary>The partition key for an accepted key, or null for a missing or unknown one.</summary>
    internal static string? Recognise(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey)) return null;
        var key = CacheKey(apiKey);
        return context.RequestServices.GetService<IMemoryCache>()?.TryGetValue(key, out _) == true ? key : null;
    }

    private static string CacheKey(string apiKey)
        => "runner-api-key:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
