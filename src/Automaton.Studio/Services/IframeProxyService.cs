using Microsoft.JSInterop;
using Serilog;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Automaton.Studio.Services;

/// <summary>
/// Service that acts as a secure proxy between iframe content and authenticated API endpoints.
/// Handles postMessage requests from embedded iframes and forwards them with proper authentication.
/// </summary>
public class IframeProxyService : IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private readonly Automaton.Client.Auth.Providers.AuthStateProvider authStateProvider;
    private readonly ILogger logger;
    private readonly ConfigurationService configService;
    private DotNetObjectReference<IframeProxyService>? dotNetRef;

    public IframeProxyService(
        HttpClient httpClient,
        Automaton.Client.Auth.Providers.AuthStateProvider authStateProvider,
        ConfigurationService configService)
    {
        this.httpClient = httpClient;
        this.authStateProvider = authStateProvider;
        this.configService = configService;
        this.logger = Log.ForContext<IframeProxyService>();
    }

    /// <summary>
    /// Creates a DotNetObjectReference for use with JSInterop.
    /// Must be called before initializing the JavaScript proxy.
    /// </summary>
    public DotNetObjectReference<IframeProxyService> CreateReference()
    {
        dotNetRef = DotNetObjectReference.Create(this);
        logger.Information("[IframeProxyService] DotNet reference created");
        return dotNetRef;
    }

    /// <summary>
    /// Handles API requests from iframe via postMessage.
    /// This method is called from JavaScript and must remain public with JSInvokable attribute.
    /// </summary>
    /// <param name="payload">The API request payload from iframe</param>
    /// <returns>API response object</returns>
    [JSInvokable("HandleApiRequest")]
    public async Task<object> HandleApiRequest(ProxyPayload payload)
    {
        logger.Information("[IframeProxyService] ===== HandleApiRequest CALLED =====");
        logger.Information("[IframeProxyService] Request ID: {RequestId}", payload.Id);
        logger.Information("[IframeProxyService] Path: {Path}", payload.Path);
        logger.Information("[IframeProxyService] Method: {Method}", payload.Method ?? "GET");
        
        try
        {
            // Validate path - only allow API calls to web-apps endpoints
            if (string.IsNullOrEmpty(payload.Path))
            {
                logger.Warning("Iframe proxy request {RequestId} rejected: empty path", payload.Id);
                return new { error = "Path is required" };
            }

            // Only allow calls to /api/web-apps/* endpoints for security
            if (!payload.Path.StartsWith("/api/web-apps/", StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning("Iframe proxy request {RequestId} rejected: invalid path {Path}", 
                    payload.Id, payload.Path);
                return new { error = "Only /api/web-apps/* endpoints are allowed" };
            }

            // Determine HTTP method
            var method = string.IsNullOrEmpty(payload.Method) || payload.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Get
                : HttpMethod.Post;

            // Build full URL using configuration service
            var baseUrl = configService.BaseUrl.TrimEnd('/');
            var fullUrl = $"{baseUrl}{payload.Path}";

            logger.Information("[IframeProxyService] Forwarding to: {Url}", fullUrl);

            // Create HTTP request
            var request = new HttpRequestMessage(method, fullUrl);

            // Add request body if present (for POST requests)
            if (!string.IsNullOrEmpty(payload.Body))
            {
                request.Content = new StringContent(payload.Body, System.Text.Encoding.UTF8, "application/json");
                logger.Information("[IframeProxyService] Request body: {Body}", payload.Body);
            }

            // Get JWT token if user is authenticated
            // For public apps, this will be empty and that's fine - the API will allow anonymous access
            var token = await authStateProvider.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                logger.Information("[IframeProxyService] Request authenticated with JWT (length: {Length})", token.Length);
            }
            else
            {
                logger.Information("[IframeProxyService] Request is anonymous (no JWT token)");
            }

            // Execute request
            logger.Information("[IframeProxyService] Sending HTTP request...");
            var response = await httpClient.SendAsync(request);
            logger.Information("[IframeProxyService] HTTP response: {StatusCode}", response.StatusCode);
            
            var responseContent = await response.Content.ReadAsStringAsync();
            logger.Information("[IframeProxyService] Response content length: {Length}", responseContent?.Length ?? 0);

            // Try to parse as JSON, fall back to plain text
            try
            {
                var jsonResponse = JsonSerializer.Deserialize<object>(responseContent);
                logger.Information("[IframeProxyService] Request {RequestId} completed successfully", payload.Id);
                return new
                {
                    status = (int)response.StatusCode,
                    statusText = response.StatusCode.ToString(),
                    data = jsonResponse,
                    success = response.IsSuccessStatusCode
                };
            }
            catch
            {
                // Not valid JSON, return as plain text
                logger.Information("[IframeProxyService] Response is not JSON, returning as text");
                return new
                {
                    status = (int)response.StatusCode,
                    statusText = response.StatusCode.ToString(),
                    data = responseContent,
                    success = response.IsSuccessStatusCode
                };
            }
        }
        catch (HttpRequestException ex)
        {
            logger.Error(ex, "[IframeProxyService] HTTP error for request {RequestId}", payload.Id);
            return new
            {
                error = "Network error",
                message = ex.Message,
                success = false
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[IframeProxyService] Unexpected error for request {RequestId}", payload.Id);
            return new
            {
                error = "Unexpected error",
                message = ex.Message,
                success = false
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        logger.Information("[IframeProxyService] Disposing");
        dotNetRef?.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Payload structure for iframe API requests.
/// Must match the structure sent from JavaScript.
/// </summary>
public class ProxyPayload
{
    /// <summary>Unique request ID for correlating request/response</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>API endpoint path (e.g., "/api/web-apps/my-app/flows/my-flow/execute")</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP method (GET or POST)</summary>
    public string? Method { get; set; }

    /// <summary>Request body as JSON string (for POST requests)</summary>
    public string? Body { get; set; }
}
