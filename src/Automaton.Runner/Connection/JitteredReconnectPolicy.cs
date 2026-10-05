using Microsoft.AspNetCore.SignalR.Client;

namespace Automaton.Runner.Console.Connection;

/// <summary>
/// Spreads simultaneous runner reconnects across a short, bounded window so an
/// API blue/green cutover does not create a synchronized connection spike.
/// </summary>
public sealed class JitteredReconnectPolicy : IRetryPolicy
{
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
        return ConnectionRetryDelay.Calculate((int)Math.Min(retryContext.PreviousRetryCount, 6),
            retryContext.RetryReason, 1, nextJitter(), DateTimeOffset.UtcNow);
    }
}
