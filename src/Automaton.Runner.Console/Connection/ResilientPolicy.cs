using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Net.Http;
using System.Net.Sockets;
using static System.Console;

namespace Automaton.Runner.Console.Connection;

public class ResilientPolicy : IConnectionPolicy
{
    private readonly int retryAfterSeconds;
    private readonly int exceptionsAllowedBeforeBreaking;
    private readonly TimeSpan durationOfBreak;

    public ResilientPolicy(int retryAfterSeconds, int exceptionsAllowedBeforeBreaking, TimeSpan durationOfBreak)
    {
        this.retryAfterSeconds = retryAfterSeconds;
        this.exceptionsAllowedBeforeBreaking = exceptionsAllowedBeforeBreaking;
        this.durationOfBreak = durationOfBreak;
    }

    public IAsyncPolicy GetPolicy()
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<SocketException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(retryAfterSeconds),
                onRetry: (exception, retry, timespan) =>
                {
                    WriteLine($"? Retry attempt {retry} in {timespan.TotalSeconds} seconds...");
                });

        var circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<SocketException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(exceptionsAllowedBeforeBreaking, durationOfBreak,
                onBreak: (exception, duration) =>
                {
                    WriteLine($"?? Connection circuit breaker opened for {duration.TotalMinutes:F1} minutes due to: {exception.Message}");
                },
                onReset: () =>
                {
                    WriteLine("?? Connection circuit breaker reset - attempting connection...");
                },
                onHalfOpen: () =>
                {
                    WriteLine("?? Connection circuit breaker half-open - testing connection...");
                });

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}