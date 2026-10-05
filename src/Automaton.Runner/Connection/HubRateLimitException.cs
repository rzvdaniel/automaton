using System.Net;

namespace Automaton.Runner.Console.Connection;

internal sealed class HubRateLimitException(DateTimeOffset? retryNotBefore)
    : HttpRequestException("Runner hub connection was rate limited.", null, HttpStatusCode.TooManyRequests)
{
    public DateTimeOffset? RetryNotBefore { get; } = retryNotBefore;
}
