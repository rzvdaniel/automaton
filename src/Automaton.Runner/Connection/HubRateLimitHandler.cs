using System.Net;
using System.Net.Http.Headers;

namespace Automaton.Runner.Console.Connection;

internal sealed class HubRateLimitHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        using (response)
        {
            DateTimeOffset? retryNotBefore = null;
            if (response.Headers.TryGetValues("Retry-After", out var values)
                && RetryConditionHeaderValue.TryParse(values.FirstOrDefault(), out var retryAfter))
            {
                retryNotBefore = retryAfter.Date
                    ?? DateTimeOffset.UtcNow.Add(retryAfter.Delta ?? TimeSpan.Zero);
            }
            throw new HubRateLimitException(retryNotBefore);
        }
    }
}
