using System.Net;

namespace Automaton.Runner.Console.Services;

/// <summary>Retries delivery of an already computed result, never its host action.</summary>
internal static class InfrastructureResultDelivery
{
    internal static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retryDelay = TimeSpan.FromSeconds(2);
            try
            {
                var response = await send(cancellationToken);
                if (!IsTransient(response.StatusCode))
                    return response;
                using (response)
                {
                    retryDelay = GetRetryDelay(response, DateTimeOffset.UtcNow);
                    Serilog.Log.Warning("Infrastructure result delivery returned HTTP {StatusCode}; retrying in {RetryDelay}.",
                        (int)response.StatusCode, retryDelay);
                }
            }
            catch (HttpRequestException exception) when (exception.StatusCode is null || IsTransient(exception.StatusCode.Value))
            {
                Serilog.Log.Warning(exception, "Infrastructure result delivery failed; retrying the existing result.");
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                Serilog.Log.Warning(exception, "Infrastructure result delivery timed out; retrying the existing result.");
            }
            await delay(retryDelay, cancellationToken);
        }
    }

    internal static TimeSpan GetRetryDelay(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta ?? (retryAfter?.Date - now);
        if (requested is null)
            return TimeSpan.FromSeconds(2);
        return requested.Value > TimeSpan.Zero ? requested.Value : TimeSpan.Zero;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
