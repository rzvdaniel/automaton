namespace Automaton.Runner.Console.Connection;

internal static class ConnectionRetryDelay
{
    internal static TimeSpan Calculate(int attempt, Exception? exception, double initialSeconds,
        double jitter, DateTimeOffset now)
    {
        var seconds = Math.Min(initialSeconds * Math.Pow(2, Math.Clamp(attempt, 0, 6))
            + Math.Clamp(jitter, 0, 1) * 5, 30);
        var delay = TimeSpan.FromSeconds(seconds);
        // SignalR can wrap transport failures. Keep the server's absolute deadline
        // so nested policies do not restart a delta-based Retry-After window.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HubRateLimitException { RetryNotBefore: { } deadline })
            {
                var requested = deadline - now;
                if (requested > delay)
                    delay = requested;
            }
        }
        // Polly/Task.Delay use millisecond timers; round up rather than retrying
        // just before the server deadline due to truncation.
        return TimeSpan.FromMilliseconds(Math.Ceiling(delay.TotalMilliseconds));
    }
}
