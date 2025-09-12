using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Net.Http;
using System.Net.Sockets;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Connection;

public class ResilientPolicy : IConnectionPolicy
{
    private readonly int retryAfterSeconds;
    private readonly int exceptionsAllowedBeforeBreaking;
    private readonly TimeSpan durationOfBreak;
    private readonly ILogger logger;

    public ResilientPolicy(int retryAfterSeconds, int exceptionsAllowedBeforeBreaking, TimeSpan durationOfBreak)
    {
        this.retryAfterSeconds = retryAfterSeconds;
        this.exceptionsAllowedBeforeBreaking = exceptionsAllowedBeforeBreaking;
        this.durationOfBreak = durationOfBreak;
        this.logger = Log.ForContext<ResilientPolicy>();
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
                    // Log technical details to file logs only
                    logger.Information(exception, "Connection retry attempt {RetryAttempt} after {Delay}s. Error: {ErrorType}", 
                        retry, timespan.TotalSeconds, exception.GetType().Name);
                    
                    // Show friendly message to user
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
                    // Log technical details to file logs only
                    logger.Warning(exception, "Connection circuit breaker opened for {Duration} due to repeated failures. Error: {ErrorType}", 
                        duration, exception.GetType().Name);
                    
                    // Show friendly message to user
                    WriteLine($"?? Connection circuit breaker opened for {duration.TotalMinutes:F1} minutes due to repeated connection failures");
                    WriteLine("   The runner will pause retry attempts to avoid overwhelming the server");
                },
                onReset: () =>
                {
                    logger.Information("Connection circuit breaker reset - connection attempts will resume");
                    WriteLine("?? Connection circuit breaker reset - attempting connection...");
                },
                onHalfOpen: () =>
                {
                    logger.Information("Connection circuit breaker half-open - testing connection stability");
                    WriteLine("?? Connection circuit breaker half-open - testing connection...");
                });

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}