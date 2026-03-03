using Automaton.Core.Models;
using System.Text;

namespace Automaton.Runner.Console.Services;

public class TunnelForwarderService
{
    private static readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://localhost:8001"),
        Timeout = TimeSpan.FromSeconds(25)
    };

    // SignalR default max message size is 32KB. Guard before sending to avoid silent failures.
    private const int MaxBodyBytes = 30 * 1024;

    public async Task<TunnelResponse> ForwardAsync(TunnelRequest request)
    {
        var tunnelResponse = new TunnelResponse { RequestId = request.RequestId };

        try
        {
            var httpRequest = new HttpRequestMessage(
                new HttpMethod(request.Method),
                request.Path);

            if (request.Body != null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

            foreach (var (k, v) in request.Headers)
                httpRequest.Headers.TryAddWithoutValidation(k, v);

            var httpResponse = await _client.SendAsync(httpRequest);

            tunnelResponse.StatusCode = (int)httpResponse.StatusCode;
            var bodyBytes = await httpResponse.Content.ReadAsByteArrayAsync();

            if (bodyBytes.Length > MaxBodyBytes)
            {
                tunnelResponse.StatusCode = 413;
                tunnelResponse.Error = $"Response body ({bodyBytes.Length} bytes) exceeds SignalR message limit ({MaxBodyBytes} bytes). Return less data or paginate.";
            }
            else
            {
                tunnelResponse.Body = Encoding.UTF8.GetString(bodyBytes);
            }
        }
        catch (Exception ex)
        {
            tunnelResponse.StatusCode = 502;
            tunnelResponse.Error = ex.Message;
        }

        return tunnelResponse;
    }
}
