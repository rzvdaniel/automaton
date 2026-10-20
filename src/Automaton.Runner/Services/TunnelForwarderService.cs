using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class TunnelForwarderService
{
    private static readonly ILogger _logger = Log.ForContext<TunnelForwarderService>();
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length"
    };

    private readonly HttpClient _fastApiClient;
    private readonly HttpClient _runtimeGatewayClient;
    private readonly HttpClient _staticSiteClient = new();
    private readonly MachineStaticSitePublisher? staticSitePublisher;
    private readonly int _maxBodyBytes;

    public TunnelForwarderService(
        IOptions<TunnelForwarderConfig> config,
        MachineStaticSitePublisher? staticSitePublisher = null)
        : this(config, null, null, staticSitePublisher)
    {
    }

    internal TunnelForwarderService(
        IOptions<TunnelForwarderConfig> config,
        HttpMessageHandler? fastApiHandler,
        HttpMessageHandler? runtimeGatewayHandler,
        MachineStaticSitePublisher? staticSitePublisher = null)
    {
        _maxBodyBytes = config.Value.MaxBodyBytes;
        this.staticSitePublisher = staticSitePublisher;
        _fastApiClient = new HttpClient(fastApiHandler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri("http://localhost:8001"),
            Timeout = TimeSpan.FromSeconds(config.Value.TimeoutSeconds)
        };
        _runtimeGatewayClient = new HttpClient(runtimeGatewayHandler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri("http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(config.Value.TimeoutSeconds)
        };
    }

    public async Task<TunnelResponse> ForwardAsync(TunnelRequest request)
    {
        try
        {
            var route = await ResolveRouteAsync(request, CancellationToken.None);
            if (route is null)
                return InvalidRuntimeDestination(request.RequestId);

            using var httpRequest = CreateHttpRequest(request, route.Value.Path);
            LogForwarding(route.Value.IsRuntimeGateway, request, route.Value.Path);
            using var httpResponse = await route.Value.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            return await CreateTunnelResponseAsync(request, httpResponse, route.Value.IsRuntimeGateway);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Tunnel call failed: {Method} {Path}", request.Method, request.Path);
            return new TunnelResponse { RequestId = request.RequestId, StatusCode = 502, Error = ex.Message };
        }
    }

    private async Task<(HttpClient Client, string Path, bool IsRuntimeGateway)?> ResolveRouteAsync(
        TunnelRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Destination, TunnelDestinations.StaticSite, StringComparison.Ordinal))
        {
            if (staticSitePublisher is null || !TryBuildStaticSitePath(request, out var staticPath)
                || request.RuntimeAppId is not { } appId || request.RuntimeDeploymentTargetId is not { } targetId)
                return null;
            var endpoint = await staticSitePublisher.GetEndpointAsync(appId, targetId, cancellationToken);
            return endpoint is null
                ? null
                : (_staticSiteClient, new Uri(endpoint.BaseAddress, staticPath.TrimStart('/')).AbsoluteUri, true);
        }

        var isRuntimeGateway = string.Equals(request.Destination, TunnelDestinations.RuntimeGateway, StringComparison.Ordinal);
        if (!isRuntimeGateway) return (_fastApiClient, request.Path, false);
        return TryBuildRuntimeGatewayPath(request, out var path)
            ? (_runtimeGatewayClient, path, true)
            : null;
    }

    private static TunnelResponse InvalidRuntimeDestination(string requestId)
        => new() { RequestId = requestId, StatusCode = 400, Error = "Runtime tunnel destination is invalid." };

    private static HttpRequestMessage CreateHttpRequest(TunnelRequest request, string requestPath)
    {
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), requestPath);
        if (request.BodyBytes != null)
            httpRequest.Content = new ByteArrayContent(request.BodyBytes);
        else if (request.Body != null)
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        foreach (var (name, value) in request.Headers)
            AddHeader(httpRequest, name, [value]);
        foreach (var (name, values) in request.HeaderValues)
            AddHeader(httpRequest, name, values);
        return httpRequest;
    }

    private static void LogForwarding(bool isRuntimeGateway, TunnelRequest request, string requestPath)
        => _logger.Debug("Forwarding through {Destination}: {Method} {Path}",
            isRuntimeGateway ? "runtime gateway" : "FastAPI", request.Method, requestPath);

    private async Task<TunnelResponse> CreateTunnelResponseAsync(
        TunnelRequest request,
        HttpResponseMessage httpResponse,
        bool isRuntimeGateway)
    {
        var bodyBytes = await httpResponse.Content.ReadAsByteArrayAsync();
        var response = new TunnelResponse { RequestId = request.RequestId, StatusCode = (int)httpResponse.StatusCode };
        if (bodyBytes.Length > _maxBodyBytes)
            return CreateOversizedResponse(request, response, bodyBytes.Length);

        CopyResponseHeaders(httpResponse, response);
        CopyResponseBody(bodyBytes, response, isRuntimeGateway);
        AddErrorSummary(request, response, bodyBytes);
        return response;
    }

    private TunnelResponse CreateOversizedResponse(TunnelRequest request, TunnelResponse response, int bodyLength)
    {
        _logger.Warning("FastAPI response body too large ({Bytes} bytes) for {Path}", bodyLength, request.Path);
        response.StatusCode = 413;
        response.Error = $"Response body ({bodyLength} bytes) exceeds SignalR message limit ({_maxBodyBytes} bytes). Return less data or paginate.";
        return response;
    }

    private static void CopyResponseHeaders(HttpResponseMessage httpResponse, TunnelResponse response)
        => response.HeaderValues = httpResponse.Headers
            .Concat(httpResponse.Content.Headers)
            .Where(header => IsForwardableResponseHeader(header.Key))
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.SelectMany(header => header.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

    private static void CopyResponseBody(byte[] bodyBytes, TunnelResponse response, bool isRuntimeGateway)
    {
        if (isRuntimeGateway)
            response.BodyBytes = bodyBytes;
        else
            response.Body = Encoding.UTF8.GetString(bodyBytes);
    }

    private static void AddErrorSummary(TunnelRequest request, TunnelResponse response, byte[] bodyBytes)
    {
        var bodyText = (response.Body ?? Encoding.UTF8.GetString(bodyBytes)).Trim();
        if (response.StatusCode < 200 || response.StatusCode >= 300)
        {
            response.Error = ExtractError(bodyText);
            _logger.Warning("FastAPI responded {StatusCode} for {Method} {Path}: {Error}",
                response.StatusCode, request.Method, request.Path, response.Error);
        }
        else
        {
            _logger.Debug("FastAPI responded {StatusCode} for {Method} {Path}", response.StatusCode, request.Method, request.Path);
        }
    }

    private static string? ExtractError(string bodyText)
    {
        if (string.IsNullOrEmpty(bodyText)) return null;
        try
        {
            using var document = JsonDocument.Parse(bodyText);
            if (document.RootElement.TryGetProperty("error", out var error) && error.GetString() is { } errorText)
                return errorText;
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { } detailText)
                return detailText;
        }
        catch (JsonException)
        {
            return LimitErrorSummary(bodyText);
        }
        return LimitErrorSummary(bodyText);
    }

    private static string LimitErrorSummary(string bodyText)
        => bodyText.Length > 500 ? bodyText[..500] + "..." : bodyText;

    internal static bool TryBuildRuntimeGatewayPath(TunnelRequest request, out string runtimeGatewayPath)
    {
        runtimeGatewayPath = string.Empty;
        if (request.RuntimeAppId is not { } appId || request.RuntimeDeploymentTargetId is not { } targetId)
            return false;
        if (!TryReadSafeRuntimePath(request.RuntimePath, out var rawPath, out var query)) return false;
        runtimeGatewayPath = $"/apps/{appId:D}/targets/{targetId:D}/{rawPath}{query}";
        return true;
    }

    internal static bool TryBuildStaticSitePath(TunnelRequest request, out string staticSitePath)
    {
        staticSitePath = string.Empty;
        if (request.RuntimeAppId is not { } appId || request.RuntimeDeploymentTargetId is null
            || !TryReadSafeRuntimePath(request.RuntimePath, out var rawPath, out var query))
            return false;
        staticSitePath = $"/{appId:D}/{rawPath}{query}";
        return true;
    }

    private static bool TryReadSafeRuntimePath(string? relativePath, out string rawPath, out string query)
    {
        rawPath = string.Empty;
        query = string.Empty;
        relativePath ??= string.Empty;
        if (relativePath.StartsWith('/') || relativePath.StartsWith('\\') || relativePath.Contains('\\')) return false;

        var queryIndex = relativePath.IndexOf('?');
        rawPath = queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
        query = queryIndex >= 0 ? relativePath[queryIndex..] : string.Empty;
        if (rawPath.Contains("://", StringComparison.Ordinal)) return false;

        foreach (var segment in rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string decodedSegment;
            try { decodedSegment = Uri.UnescapeDataString(segment); }
            catch (UriFormatException) { return false; }
            if (decodedSegment is "." or ".." || decodedSegment.Contains('\\') || decodedSegment.Contains('/')) return false;
        }
        return true;
    }

    internal static bool IsForwardableResponseHeader(string name)
        => !HopByHopHeaders.Contains(name);

    private static void AddHeader(HttpRequestMessage request, string name, IEnumerable<string> values)
    {
        if (!request.Headers.TryAddWithoutValidation(name, values) && request.Content != null)
            request.Content.Headers.TryAddWithoutValidation(name, values);
    }
}
