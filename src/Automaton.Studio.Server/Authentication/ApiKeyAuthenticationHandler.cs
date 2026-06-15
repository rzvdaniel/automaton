using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
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
    private readonly ApplicationDbContext dbContext;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService,
        ApplicationDbContext dbContext) : base(options, logger, encoder)
    {
        this.apiKeyService = apiKeyService;
        this.dbContext = dbContext;
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
            Logger.LogWarning("Invalid API key attempted for request {Method} {Path}", Request.Method, Request.Path);
            return AuthenticateResult.Fail("Invalid API key");
        }

        // Get the user associated with this API key
        var userId = await dbContext.ApiKeyUsers
            .Where(aku => aku.ApiKeyId == apiKeyEntity.Id)
            .Select(aku => aku.UserId)
            .FirstOrDefaultAsync();
        
        if (userId == Guid.Empty)
        {
            Logger.LogWarning("API key {ApiKeyId} has no associated user", apiKeyEntity.Id);
            return AuthenticateResult.Fail("Invalid API key - no user association");
        }
        
        var user = await dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            Logger.LogWarning("User {UserId} not found for API key {ApiKeyId}", userId, apiKeyEntity.Id);
            return AuthenticateResult.Fail("Invalid API key - user not found");
        }

        Logger.LogDebug("API key authentication succeeded for user {UserId} with scope {Scope} on request {Method} {Path}",
            userId, apiKeyEntity.Scope, Request.Method, Request.Path);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email),
            new(ClaimTypes.Email, user.Email),
            new("api_key_scope", apiKeyEntity.Scope.ToString()),
            new("api_key_id", apiKeyEntity.Id.ToString()),
            new("uid", userId.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
