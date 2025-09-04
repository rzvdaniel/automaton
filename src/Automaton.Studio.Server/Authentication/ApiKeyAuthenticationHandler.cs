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
            return AuthenticateResult.NoResult();
        }

        var apiKeyEntity = await apiKeyService.ValidateApiKeyAsync(apiKey);
        if (apiKeyEntity == null)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }

        var claims = new List<Claim>
        {
            new("uid", apiKeyEntity.UserId.ToString()),
            new(ClaimTypes.Name, apiKeyEntity.User.UserName ?? apiKeyEntity.User.Email),
            new(ClaimTypes.Email, apiKeyEntity.User.Email),
            new("api_key_scope", apiKeyEntity.Scope.ToString()),
            new("api_key_id", apiKeyEntity.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}