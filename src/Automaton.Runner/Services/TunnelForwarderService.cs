using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class TunnelForwarderService
{
    private static readonly ILogger _logger = Log.ForContext<TunnelForwarderService>();

    private readonly HttpClient _fastApiClient;
    private readonly HttpClient _runtimeGatewayClient;
    private readonly int _maxBodyBytes;

    public TunnelForwarderService(IOptions<TunnelForwarderConfig> config)
    {
        _maxBodyBytes = config.Value.MaxBodyBytes;
        _fastApiClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8001"),
            Timeout = TimeSpan.FromSeconds(config.Value.TimeoutSeconds)
        };
        _runtimeGatewayClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(config.Value.TimeoutSeconds)
        };
    }

    public async Task<TunnelResponse> ForwardAsync(TunnelRequest request)
    {
        var tunnelResponse = new TunnelResponse { RequestId = request.RequestId };

        try
        {
            var isRuntimeGateway = string.Equals(
                request.Destination,
                TunnelDestinations.RuntimeGateway,
                StringComparison.Ordinal);
            var client = isRuntimeGateway ? _runtimeGatewayClient : _fastApiClient;

            if (isRuntimeGateway && !IsFixedRuntimeGatewayPath(request))
            {
                tunnelResponse.StatusCode = 400;
                tunnelResponse.Error = "Runtime tunnel destination is invalid.";
                return tunnelResponse;
            }

            using var httpRequest = new HttpRequestMessage(
                new HttpMethod(request.Method),
                request.Path);

            if (request.BodyBytes != null)
                httpRequest.Content = new ByteArrayContent(request.BodyBytes);
            else if (request.Body != null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

            foreach (var (k, v) in request.Headers)
                AddHeader(httpRequest, k, [v]);
            foreach (var (k, values) in request.HeaderValues)
                AddHeader(httpRequest, k, values);

            _logger.Debug("Forwarding through {Destination}: {Method} {Path}",
                isRuntimeGateway ? "runtime gateway" : "FastAPI", request.Method, request.Path);

            using var httpResponse = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

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
                tunnelResponse.HeaderValues = httpResponse.Headers
                    .Concat(httpResponse.Content.Headers)
                    .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.SelectMany(header => header.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

                if (isRuntimeGateway)
                    tunnelResponse.BodyBytes = bodyBytes;
                else
                    tunnelResponse.Body = Encoding.UTF8.GetString(bodyBytes);

                if (tunnelResponse.StatusCode < 200 || tunnelResponse.StatusCode >= 300)
                {
                    // Extract a human-readable error from the response body so callers
                    // can surface it in logs instead of showing Error=null.
                    var bodyText = (tunnelResponse.Body ?? Encoding.UTF8.GetString(bodyBytes)).Trim();
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
            _logger.Error(ex, "Tunnel call failed: {Method} {Path}", request.Method, request.Path);
            tunnelResponse.StatusCode = 502;
            tunnelResponse.Error = ex.Message;
        }

        return tunnelResponse;
    }

    private static bool IsFixedRuntimeGatewayPath(TunnelRequest request)
    {
        if (request.RuntimeAppId is not { } appId || request.RuntimeDeploymentTargetId is not { } targetId)
            return false;

        var expectedPrefix = $"/apps/{appId:D}/targets/{targetId:D}/";
        return request.Path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            && !request.Path.Contains("://", StringComparison.Ordinal);
    }

    private static void AddHeader(HttpRequestMessage request, string name, IEnumerable<string> values)
    {
        if (!request.Headers.TryAddWithoutValidation(name, values) && request.Content != null)
            request.Content.Headers.TryAddWithoutValidation(name, values);
    }
}
