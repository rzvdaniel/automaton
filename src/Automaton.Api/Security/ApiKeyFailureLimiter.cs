using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Security;

/// <summary>
/// Counts wrong API keys per client address. Past the limit the address is answered 429 (see <see cref="ApiKeyFailureMiddleware"/>)
/// until the window ends, so keys cannot be guessed; the 429s are what the fail2ban sign-in jail bans on.
/// </summary>
public sealed class ApiKeyFailureLimiter(IOptions<RateLimitingOptions> options)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset WindowEnd, int Count)> attempts = new();

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    public void RecordFailure(HttpContext context)
    {
        var policy = options.Value.ApiKeyFailures;
        var now = Clock.GetUtcNow();
        if (attempts.Count > 10_000) Sweep(now);
        attempts.AddOrUpdate(Key(context), _ => (now.AddSeconds(policy.WindowSeconds), 1),
            (_, current) => current.WindowEnd <= now ? (now.AddSeconds(policy.WindowSeconds), 1) : (current.WindowEnd, current.Count + 1));
    }

    public bool IsBlocked(HttpContext context)
        => attempts.TryGetValue(Key(context), out var current)
            && current.WindowEnd > Clock.GetUtcNow()
            && current.Count >= options.Value.ApiKeyFailures.PermitLimit;

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in attempts.Where(p => p.Value.WindowEnd <= now)) attempts.TryRemove(pair.Key, out _);
    }

    private static string Key(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>Answers 429 to a request that carries an API key the rate limiter has not seen accepted, from an address that sent too many wrong ones.</summary>
public sealed class ApiKeyFailureMiddleware(RequestDelegate next, ApiKeyFailureLimiter limiter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HasApiKey(context.Request) && RateLimitedApiKeys.Recognise(context) is null && limiter.IsBlocked(context))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsJsonAsync(new { message = "Too many requests. Please try again later." });
            return;
        }

        await next(context);
    }

    private static bool HasApiKey(HttpRequest request)
        => request.Headers.ContainsKey("X-API-Key")
            || request.Query.ContainsKey("api_key")
            || request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ak_", StringComparison.Ordinal) == true;
}
