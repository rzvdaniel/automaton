using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Rate limiting service for system AI requests.
/// Uses sliding window algorithm with in-memory cache.
/// Shared across utility endpoints and chat conversations.
/// </summary>
public class SystemAiRateLimitService : ISystemAiRateLimitService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemAiRateLimitService> _logger;

    private const string RateLimitKeyPrefix = "SystemAi:RateLimit:";
    private const int MaxRequestsPerUserPerMinute = 10;

    public SystemAiRateLimitService(
        IMemoryCache cache,
        ILogger<SystemAiRateLimitService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CheckAndRecordRequest(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return true; // Anonymous users not rate limited (shouldn't happen with [Authorize])

        var cacheKey = $"{RateLimitKeyPrefix}{userId}";
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);

        // Get or create request timestamps list
        var timestamps = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new List<DateTime>();
        })!;

        // Thread-safe access
        lock (timestamps)
        {
            // Remove expired timestamps
            timestamps.RemoveAll(t => t < windowStart);

            // Check if under limit
            if (timestamps.Count >= MaxRequestsPerUserPerMinute)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for user {UserId}: {RequestCount}/{MaxRequests} requests in last minute",
                    userId, timestamps.Count, MaxRequestsPerUserPerMinute);
                return false;
            }

            // Add current request timestamp
            timestamps.Add(now);
            _cache.Set(cacheKey, timestamps, TimeSpan.FromMinutes(2));

            _logger.LogDebug(
                "Rate limit check passed for user {UserId}: {RequestCount}/{MaxRequests} requests",
                userId, timestamps.Count, MaxRequestsPerUserPerMinute);

            return true;
        }
    }

    /// <inheritdoc />
    public int GetRemainingRequests(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return MaxRequestsPerUserPerMinute;

        var cacheKey = $"{RateLimitKeyPrefix}{userId}";
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);

        if (!_cache.TryGetValue(cacheKey, out List<DateTime>? timestamps) || timestamps == null)
            return MaxRequestsPerUserPerMinute;

        lock (timestamps)
        {
            var validCount = timestamps.Count(t => t >= windowStart);
            return Math.Max(0, MaxRequestsPerUserPerMinute - validCount);
        }
    }
}
