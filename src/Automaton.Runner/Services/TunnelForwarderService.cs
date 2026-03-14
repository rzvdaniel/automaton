using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class TunnelForwarderService
{
    private static readonly ILogger _logger = Log.ForContext<TunnelForwarderService>();

    private readonly HttpClient _client;
    private readonly int _maxBodyBytes;

    public TunnelForwarderService(IOptions<TunnelForwarderConfig> config)
    {
        _maxBodyBytes = config.Value.MaxBodyBytes;
        _client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8001"),
            Timeout = TimeSpan.FromSeconds(config.Value.TimeoutSeconds)
        };
    }

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

            _logger.Debug("Forwarding to FastAPI: {Method} {Path}", request.Method, request.Path);

            var httpResponse = await _client.SendAsync(httpRequest);

            tunnelResponse.StatusCode = (int)httpResponse.StatusCode;
            var bodyBytes = await httpResponse.Content.ReadAsByteArrayAsync();

            if (bodyBytes.Length > _maxBodyBytes)
            {
                _logger.Warning("FastAPI response body too large ({Bytes} bytes) for {Path}", bodyBytes.Length, request.Path);
                tunnelResponse.StatusCode = 413;
                tunnelResponse.Error = $"Response body ({bodyBytes.Length} bytes) exceeds SignalR message limit ({_maxBodyBytes} bytes). Return less data or paginate.";
            }
            else
            {
                tunnelResponse.Body = Encoding.UTF8.GetString(bodyBytes);

                if (tunnelResponse.StatusCode < 200 || tunnelResponse.StatusCode >= 300)
                {
                    // Extract a human-readable error from the response body so callers
                    // can surface it in logs instead of showing Error=null.
                    var bodyText = tunnelResponse.Body?.Trim();
                    if (!string.IsNullOrEmpty(bodyText))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(bodyText);
                            if (doc.RootElement.TryGetProperty("error", out var errEl))
                                tunnelResponse.Error = errEl.GetString();
                            else if (doc.RootElement.TryGetProperty("detail", out var detEl))
                                tunnelResponse.Error = detEl.GetString();
                        }
                        catch { /* not JSON */ }

                        if (tunnelResponse.Error == null)
                            tunnelResponse.Error = bodyText.Length > 500 ? bodyText[..500] + "..." : bodyText;
                    }
                    _logger.Warning("FastAPI responded {StatusCode} for {Method} {Path}: {Error}",
                        tunnelResponse.StatusCode, request.Method, request.Path, tunnelResponse.Error);
                }
                else
                {
                    _logger.Debug("FastAPI responded {StatusCode} for {Method} {Path}", tunnelResponse.StatusCode, request.Method, request.Path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FastAPI call failed: {Method} {Path}", request.Method, request.Path);
            tunnelResponse.StatusCode = 502;
            tunnelResponse.Error = ex.Message;
        }

        return tunnelResponse;
    }
}
