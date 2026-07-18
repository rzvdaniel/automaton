using Microsoft.AspNetCore.SignalR.Client;

namespace Automaton.Runner.Console.Connection;

/// <summary>
/// Spreads simultaneous runner reconnects across a short, bounded window so an
/// API blue/green cutover does not create a synchronized connection spike.
/// </summary>
public sealed class JitteredReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);
    private readonly Func<double> nextJitter;

    public JitteredReconnectPolicy()
        : this(() => Random.Shared.NextDouble())
    {
    }

    public JitteredReconnectPolicy(Func<double> nextJitter)
    {
        this.nextJitter = nextJitter ?? throw new ArgumentNullException(nameof(nextJitter));
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var exponentialSeconds = Math.Min(
            Math.Pow(2, retryContext.PreviousRetryCount),
            MaximumDelay.TotalSeconds);
        var jitterSeconds = Math.Clamp(nextJitter(), 0, 1) * 5;

        return TimeSpan.FromSeconds(
            Math.Min(exponentialSeconds + jitterSeconds, MaximumDelay.TotalSeconds));
    }
}
