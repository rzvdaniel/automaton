using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Automaton.Api.Services;
using Automaton.Api.Authentication.GitHub;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Security;
using Automaton.Api.Authentication;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("auth/github")]
public class GitHubAuthController : ControllerBase
{
    private const string ReturnUrlItemKey = "returnUrl";

    private readonly AuthTokenService _authTokenService;
    private readonly ConfigurationService _configurationService;
    private readonly GitHubOAuthOptions _gitHubOptions;
    private readonly ILogger<GitHubAuthController> _logger;
    private readonly RefreshTokenCookieService _refreshTokenCookieService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UsersService _usersService;

    public GitHubAuthController(
        AuthTokenService authTokenService,
        ConfigurationService configurationService,
        IOptions<GitHubOAuthOptions> gitHubOptions,
        ILogger<GitHubAuthController> logger,
        RefreshTokenCookieService refreshTokenCookieService,
        SignInManager<ApplicationUser> signInManager,
        UsersService usersService)
    {
        _authTokenService = authTokenService;
        _configurationService = configurationService;
        _gitHubOptions = gitHubOptions.Value;
        _logger = logger;
        _refreshTokenCookieService = refreshTokenCookieService;
        _signInManager = signInManager;
        _usersService = usersService;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var sanitizedReturnUrl = NormalizeReturnUrl(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.ActionLink(nameof(Callback))!
        };

        properties.Items[ReturnUrlItemKey] = sanitizedReturnUrl;

        return Challenge(properties, GitHubAuthenticationDefaults.Scheme);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var externalAuth = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!externalAuth.Succeeded || externalAuth.Principal == null)
        {
            return Redirect(BuildFrontendErrorRedirect("GitHub authentication failed."));
        }

        try
        {
            var principal = externalAuth.Principal;
            var verifiedEmail = principal.FindFirstValue(ClaimTypes.Email);
            var githubUsername = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var githubDisplayName = principal.FindFirstValue(ClaimTypes.Name);
            var returnUrl = externalAuth.Properties?.Items.TryGetValue(ReturnUrlItemKey, out var storedReturnUrl) == true
                ? storedReturnUrl
                : "/";

            if (string.IsNullOrWhiteSpace(verifiedEmail))
            {
                return Redirect(BuildFrontendErrorRedirect("Your GitHub account must expose a verified email address."));
            }

            if (string.IsNullOrWhiteSpace(githubUsername))
            {
                return Redirect(BuildFrontendErrorRedirect("GitHub did not return a username."));
            }

            var user = await _usersService.FindByEmailAsync(verifiedEmail);
            if (user?.IsDeleted == true)
            {
                return Redirect(BuildFrontendErrorRedirect("This account is no longer available."));
            }

            if (user == null)
            {
                var publicUsername = await _usersService.GenerateUniquePublicUsernameFromCandidateAsync(githubUsername);
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    Email = verifiedEmail,
                    UserName = verifiedEmail,
                    Name = string.IsNullOrWhiteSpace(githubDisplayName) ? githubUsername : githubDisplayName,
                    PublicUsername = publicUsername,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    RegisteredAtUtc = DateTime.UtcNow,
                    RegistrationProvider = RegistrationProviders.GitHub
                };

                await _usersService.CreateUserWithoutPasswordAsync(user);
            }

            await _usersService.MarkSuccessfulLoginAsync(user);
            await _signInManager.SignInAsync(user, isPersistent: false);

            var jwt = await _authTokenService.IssueForUserAsync(
                user,
                _configurationService.RefreshTokenLifetime,
                cancellationToken);
            _refreshTokenCookieService.AppendRefreshToken(HttpContext, jwt.RefreshToken, _configurationService.RefreshTokenLifetime);
            jwt.RefreshToken = string.Empty;

            return Redirect(BuildFrontendSuccessRedirect(jwt, returnUrl ?? "/"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub OAuth callback failed.");
            return Redirect(BuildFrontendErrorRedirect("GitHub login failed."));
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private string BuildFrontendSuccessRedirect(JsonWebToken jwt, string returnUrl)
    {
        var callbackUrl = BuildFrontendCallbackBaseUrl();
        var fragment = QueryHelpers.AddQueryString(
            string.Empty,
            new Dictionary<string, string?>
            {
                ["accessToken"] = jwt.AccessToken,
                ["redirect"] = NormalizeReturnUrl(returnUrl)
            });

        return $"{callbackUrl}#{fragment.TrimStart('?')}";
    }

    private string BuildFrontendErrorRedirect(string message)
    {
        return QueryHelpers.AddQueryString(
            BuildFrontendCallbackBaseUrl(),
            "error",
            message);
    }

    private string BuildFrontendCallbackBaseUrl()
    {
        var frontendBaseUrl = string.IsNullOrWhiteSpace(_gitHubOptions.FrontendUrl)
            ? _configurationService.WebsiteUrl
            : _gitHubOptions.FrontendUrl;

        var baseUri = new Uri(frontendBaseUrl.TrimEnd('/'));
        return new Uri(baseUri, "auth/github/callback").ToString();
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            return "/";
        }

        return returnUrl;
    }
}
