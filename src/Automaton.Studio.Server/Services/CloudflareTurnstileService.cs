using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class CloudflareTurnstileService : ICloudflareTurnstileService
{
    private const string SiteVerifyEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly CloudflareTurnstileOptions options;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<CloudflareTurnstileService> logger;

    public CloudflareTurnstileService(
        HttpClient httpClient,
        IOptions<CloudflareTurnstileOptions> options,
        IWebHostEnvironment environment,
        ILogger<CloudflareTurnstileService> logger)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.environment = environment;
        this.logger = logger;
    }

    public async Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            if (environment.IsDevelopment() && options.AllowBypassWhenNotConfiguredInDevelopment)
            {
                logger.LogDebug("Cloudflare Turnstile secret key is not configured. Bypassing verification in development.");
                return true;
            }

            logger.LogWarning("Cloudflare Turnstile is enabled but the secret key is not configured.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SiteVerifyEndpoint)
        {
            Content = new FormUrlEncodedContent(BuildRequestBody(token, remoteIp))
        };

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cloudflare Turnstile verification request failed with status code {StatusCode}.", response.StatusCode);
                return false;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TurnstileSiteVerifyResponse>(
                responseStream,
                JsonSerializerOptions,
                cancellationToken);
            return payload?.Success == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cloudflare Turnstile verification request failed.");
            return false;
        }
    }

    private Dictionary<string, string> BuildRequestBody(string token, string? remoteIp)
    {
        var body = new Dictionary<string, string>
        {
            ["secret"] = options.SecretKey,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            body["remoteip"] = remoteIp;
        }

        return body;
    }

    private sealed class TurnstileSiteVerifyResponse
    {
        public bool Success { get; set; }
    }
}
