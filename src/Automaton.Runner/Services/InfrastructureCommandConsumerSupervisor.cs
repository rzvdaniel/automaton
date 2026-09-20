using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>Owns retries for complete Infrastructure Command consumer attempts.</summary>
internal sealed class InfrastructureCommandConsumerSupervisor(
    Func<CancellationToken, Task> consumeAttempt,
    TimeSpan retryDelay,
    ILogger logger)
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumeAttempt(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Infrastructure command consumer stopped; retrying with a fresh RabbitMQ attempt after {RetryDelaySeconds} seconds.",
                    retryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
