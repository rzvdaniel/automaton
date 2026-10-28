using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Automaton.Api.Authentication;

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
    private readonly UserManager<ApplicationUser> userManager;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService,
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager) : base(options, logger, encoder)
    {
        this.apiKeyService = apiKeyService;
        this.dbContext = dbContext;
        this.userManager = userManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = GetApiKeyFromRequest();

        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateResult.NoResult();
        }

        return await AuthenticateApiKeyAsync(apiKey);
    }

    private string? GetApiKeyFromRequest()
    {
        var apiKey = Request.Headers[Options.HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Request.Query[Options.QueryParameterName].FirstOrDefault();

        if (!string.IsNullOrEmpty(apiKey))
            return apiKey;

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        return authHeader?.StartsWith("Bearer ak_") == true
            ? authHeader["Bearer ".Length..]
            : null;
    }

    private async Task<AuthenticateResult> AuthenticateApiKeyAsync(string apiKey)
    {
        var apiKeyEntity = await apiKeyService.ValidateApiKeyAsync(apiKey);
        if (apiKeyEntity == null)
        {
            Logger.LogWarning("Invalid API key attempted for request {Method} {Path}", Request.Method, Request.Path);
            return AuthenticateResult.Fail("Invalid API key");
        }

        var (userId, user) = await LoadApiKeyUserAsync(apiKeyEntity.Id);
        if (userId == Guid.Empty)
        {
            Logger.LogWarning("API key {ApiKeyId} has no associated user", apiKeyEntity.Id);
            return AuthenticateResult.Fail("Invalid API key - no user association");
        }

        if (user == null)
        {
            Logger.LogWarning("User {UserId} not found for API key {ApiKeyId}", userId, apiKeyEntity.Id);
            return AuthenticateResult.Fail("Invalid API key - user not found");
        }

        Logger.LogDebug("API key authentication succeeded for user {UserId} with scope {Scope} on request {Method} {Path}",
            userId, apiKeyEntity.Scope, Request.Method, Request.Path);

        return await CreateAuthenticationResultAsync(apiKeyEntity, userId, user);
    }

    private async Task<(Guid UserId, ApplicationUser? User)> LoadApiKeyUserAsync(Guid apiKeyId)
    {
        var userId = await dbContext.ApiKeyUsers
            .Where(aku => aku.ApiKeyId == apiKeyId)
            .Select(aku => aku.UserId)
            .FirstOrDefaultAsync();
        var user = userId == Guid.Empty ? null : await dbContext.Users.FindAsync(userId);
        return (userId, user);
    }

    private async Task<AuthenticateResult> CreateAuthenticationResultAsync(
        ApiKey apiKeyEntity,
        Guid userId,
        ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email),
            new(ClaimTypes.Email, user.Email),
            new("api_key_scope", apiKeyEntity.Scope.ToString()),
            new("api_key_id", apiKeyEntity.Id.ToString()),
            new("uid", userId.ToString())
        };

        AddRoleClaims(claims, await userManager.GetRolesAsync(user));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    internal static void AddRoleClaims(ICollection<Claim> claims, IEnumerable<string> roles)
    {
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
    }
}
