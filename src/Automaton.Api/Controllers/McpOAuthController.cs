using System.Text.Json.Serialization;
using Automaton.Api.Constants;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Api.Controllers;

/// <summary>
/// Sign-in for AI connectors to /mcp (Claude custom connectors): OAuth 2.1 discovery, dynamic client registration, the
/// authorization redirect to Studio's consent page, and the token endpoint. See <see cref="McpOAuthService"/>.
/// </summary>
[ApiController]
public sealed class McpOAuthController : ControllerBase
{
    private readonly McpOAuthService oauth;
    private readonly ConfigurationService configurationService;
    private readonly UserContextService userContextService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;

    public McpOAuthController(
        McpOAuthService oauth,
        ConfigurationService configurationService,
        UserContextService userContextService,
        ConfirmedEmailGuardService confirmedEmailGuardService)
    {
        this.oauth = oauth;
        this.configurationService = configurationService;
        this.userContextService = userContextService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
    }

    private string ApiBase => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("/.well-known/oauth-protected-resource")]
    [HttpGet("/.well-known/oauth-protected-resource/mcp")]
    [AllowAnonymous]
    public IActionResult ProtectedResource() => Ok(new
    {
        resource = $"{ApiBase}/mcp",
        authorization_servers = new[] { ApiBase },
        bearer_methods_supported = new[] { "header" }
    });

    [HttpGet("/.well-known/oauth-authorization-server")]
    [AllowAnonymous]
    public IActionResult Metadata() => Ok(new
    {
        issuer = ApiBase,
        authorization_endpoint = $"{ApiBase}/oauth/authorize",
        token_endpoint = $"{ApiBase}/oauth/token",
        registration_endpoint = $"{ApiBase}/oauth/register",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" }
    });

    [HttpPost("/oauth/register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public IActionResult Register([FromBody] RegisterClientRequest request)
    {
        var registered = oauth.Register(request.ClientName, request.RedirectUris);
        if (registered is null)
            return BadRequest(new { error = "invalid_redirect_uri", error_description = "Redirect URIs must be https (or http on localhost), without a fragment." });

        return StatusCode(StatusCodes.Status201Created, new
        {
            client_id = registered.Value.ClientId,
            client_name = registered.Value.Info.Name,
            redirect_uris = registered.Value.Info.RedirectUris,
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none"
        });
    }

    /// <summary>Checks the request and sends the browser on to Studio's consent page. Bad clients are never redirected back.</summary>
    [HttpGet("/oauth/authorize")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public IActionResult Authorize()
    {
        var q = Request.Query;
        if (q["response_type"] != "code"
            || oauth.ValidateAuthorization(q["client_id"], q["redirect_uri"], q["code_challenge"], q["code_challenge_method"]) is null)
            return BadRequest("This sign-in link is invalid. Remove the connector and add it again.");

        var studio = string.IsNullOrWhiteSpace(configurationService.WebsiteUrl) ? ApiBase : configurationService.WebsiteUrl.TrimEnd('/');
        var query = QueryString.Create(new[] { "client_id", "redirect_uri", "code_challenge", "code_challenge_method", "state" }
            .Select(k => new KeyValuePair<string, string?>(k, q[k].ToString())));
        return Redirect($"{studio}/mcp/authorize{query}");
    }

    /// <summary>What the consent page shows: the client's chosen name and where it will send the browser.</summary>
    [HttpGet("api/mcp-oauth/request")]
    [Authorize]
    public IActionResult Describe([FromQuery] AuthorizeParameters p)
    {
        var client = oauth.ValidateAuthorization(p.ClientId, p.RedirectUri, p.CodeChallenge, p.CodeChallengeMethod);
        return client is null
            ? NotFound(new { message = "This sign-in request is invalid." })
            : Ok(new { clientName = client.Name, redirectHost = new Uri(p.RedirectUri!).Host });
    }

    /// <summary>The signed-in user approves: returns where to send the browser, carrying the one-time code.</summary>
    [HttpPost("api/mcp-oauth/approve")]
    [Authorize]
    public async Task<IActionResult> Approve([FromBody] AuthorizeParameters p)
    {
        if (User.FindFirst("api_key_scope") is not null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Sign in to Studio with your account to approve a connector." });
        var userId = userContextService.GetUserId();
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userId))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        if (oauth.ValidateAuthorization(p.ClientId, p.RedirectUri, p.CodeChallenge, p.CodeChallengeMethod) is null)
            return BadRequest(new { message = "This sign-in request is invalid." });

        var code = oauth.IssueCode(userId, p.ClientId!, p.RedirectUri!, p.CodeChallenge!);
        var separator = p.RedirectUri!.Contains('?') ? '&' : '?';
        var redirect = $"{p.RedirectUri}{separator}code={Uri.EscapeDataString(code)}"
            + (string.IsNullOrEmpty(p.State) ? string.Empty : $"&state={Uri.EscapeDataString(p.State)}");
        return Ok(new { redirectUrl = redirect });
    }

    /// <summary>The user denied: sends the browser back with access_denied.</summary>
    [HttpPost("api/mcp-oauth/deny")]
    [Authorize]
    public IActionResult Deny([FromBody] AuthorizeParameters p)
    {
        if (oauth.ValidateAuthorization(p.ClientId, p.RedirectUri, p.CodeChallenge, p.CodeChallengeMethod) is null)
            return BadRequest(new { message = "This sign-in request is invalid." });
        var separator = p.RedirectUri!.Contains('?') ? '&' : '?';
        return Ok(new { redirectUrl = $"{p.RedirectUri}{separator}error=access_denied"
            + (string.IsNullOrEmpty(p.State) ? string.Empty : $"&state={Uri.EscapeDataString(p.State)}") });
    }

    [HttpPost("/oauth/token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var result = request.GrantType switch
        {
            "authorization_code" => await oauth.ExchangeCodeAsync(request.Code, request.CodeVerifier, request.ClientId, request.RedirectUri),
            "refresh_token" => await oauth.RefreshAsync(request.RefreshToken, request.ClientId),
            _ => null
        };
        if (request.GrantType is not ("authorization_code" or "refresh_token"))
            return BadRequest(new { error = "unsupported_grant_type" });
        if (result is null)
            return BadRequest(new { error = "invalid_grant", error_description = "The code or refresh token is invalid, used or expired." });

        return Ok(new
        {
            access_token = result.AccessToken,
            token_type = "Bearer",
            expires_in = result.ExpiresIn,
            refresh_token = result.RefreshToken
        });
    }
}

public sealed class RegisterClientRequest
{
    [JsonPropertyName("client_name")] public string? ClientName { get; set; }
    [JsonPropertyName("redirect_uris")] public List<string>? RedirectUris { get; set; }
}

public sealed class AuthorizeParameters
{
    [FromQuery(Name = "client_id")] [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [FromQuery(Name = "redirect_uri")] [JsonPropertyName("redirect_uri")] public string? RedirectUri { get; set; }
    [FromQuery(Name = "code_challenge")] [JsonPropertyName("code_challenge")] public string? CodeChallenge { get; set; }
    [FromQuery(Name = "code_challenge_method")] [JsonPropertyName("code_challenge_method")] public string? CodeChallengeMethod { get; set; }
    [FromQuery(Name = "state")] [JsonPropertyName("state")] public string? State { get; set; }
}

public sealed class TokenRequest
{
    [FromForm(Name = "grant_type")] public string? GrantType { get; set; }
    [FromForm(Name = "code")] public string? Code { get; set; }
    [FromForm(Name = "code_verifier")] public string? CodeVerifier { get; set; }
    [FromForm(Name = "client_id")] public string? ClientId { get; set; }
    [FromForm(Name = "redirect_uri")] public string? RedirectUri { get; set; }
    [FromForm(Name = "refresh_token")] public string? RefreshToken { get; set; }
}
