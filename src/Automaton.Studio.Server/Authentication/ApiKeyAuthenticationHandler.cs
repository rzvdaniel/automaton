using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Automaton.Studio.Server.Authentication;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
    public string Scheme => DefaultScheme;
    public string HeaderName { get; set; } = "X-API-Key";
    public string QueryParameterName { get; set; } = "api_key";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ApiKeyService apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService) : base(options, logger, encoder)
    {
        this.apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Logger.LogDebug("API Key authentication handler called for request: {Method} {Path}", Request.Method, Request.Path);
        
        // Try to get API key from header first
        var apiKey = Request.Headers[Options.HeaderName].FirstOrDefault();
        
        // If not in header, try query parameter
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = Request.Query[Options.QueryParameterName].FirstOrDefault();
        }

        // If still no API key, check Authorization header for Bearer token format
        if (string.IsNullOrEmpty(apiKey))
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ak_") == true)
            {
                apiKey = authHeader["Bearer ".Length..];
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogDebug("No API key found in request headers or query parameters for {Method} {Path}", Request.Method, Request.Path);
            return AuthenticateResult.NoResult();
        }

        Logger.LogDebug("API key found, validating: {KeyPrefix}... for request {Method} {Path}", 
            apiKey.Length > 15 ? apiKey[..15] : apiKey, Request.Method, Request.Path);

        var apiKeyEntity = await apiKeyService.ValidateApiKeyAsync(apiKey);
        if (apiKeyEntity == null)
        {
            Logger.LogWarning("Invalid API key attempted: {KeyPrefix}... for request {Method} {Path}", 
                apiKey.Length > 15 ? apiKey[..15] : apiKey, Request.Method, Request.Path);
            return AuthenticateResult.Fail("Invalid API key");
        }

        Logger.LogInformation("API key authentication successful for user {UserId} with scope {Scope} on request {Method} {Path}", 
            apiKeyEntity.UserId, apiKeyEntity.Scope, Request.Method, Request.Path);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKeyEntity.UserId.ToString()), // Use ClaimTypes.NameIdentifier instead of "uid"
            new(ClaimTypes.Name, apiKeyEntity.User.UserName ?? apiKeyEntity.User.Email),
            new(ClaimTypes.Email, apiKeyEntity.User.Email),
            new("api_key_scope", apiKeyEntity.Scope.ToString()),
            new("api_key_id", apiKeyEntity.Id.ToString()),
            new("uid", apiKeyEntity.UserId.ToString()) // Keep "uid" for backwards compatibility with other parts
        };

        Logger.LogDebug("Created claims for API key authentication: NameIdentifier={UserId}, Name={UserName}, Email={Email}", 
            apiKeyEntity.UserId, apiKeyEntity.User.UserName ?? apiKeyEntity.User.Email, apiKeyEntity.User.Email);

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}