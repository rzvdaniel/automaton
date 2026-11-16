using System.Text;

namespace Automaton.Api.Services;

/// <summary>A response the API answers with itself because the request never reached the Runner.</summary>
public sealed record TunnelFailure(
    int StatusCode,
    string Body,
    string ContentType = "text/plain; charset=utf-8",
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public static TunnelFailure Text(int statusCode, string error) => new(statusCode, error);

    public async Task WriteAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCode;
        response.ContentType = ContentType;
        foreach (var (name, value) in Headers ?? new Dictionary<string, string>())
            response.Headers[name] = value;
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(Body), cancellationToken);
    }
}
