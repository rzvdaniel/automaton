using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Authentication.GitHub;

public static class GitHubAuthenticationServiceCollectionExtensions
{
    public static AuthenticationBuilder AddGitHubOAuth(this AuthenticationBuilder authenticationBuilder, IConfiguration configuration)
    {
        return authenticationBuilder.AddOAuth(GitHubAuthenticationDefaults.Scheme, options =>
        {
            var githubOptions = configuration.GetSection("GitHub").Get<GitHubOAuthOptions>() ?? new GitHubOAuthOptions();
            ConfigureOAuthOptions(options, githubOptions);
        });
    }

    private static void ConfigureOAuthOptions(OAuthOptions options, GitHubOAuthOptions githubOptions)
    {
        options.ClientId = githubOptions.ClientId;
        options.ClientSecret = githubOptions.ClientSecret;
        options.CallbackPath = "/signin-github";
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.SaveTokens = true;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Scope.Add("read:user");
        options.Scope.Add("user:email");

        options.Events.OnCreatingTicket = HandleCreatingTicketAsync;
        options.Events.OnRedirectToAuthorizationEndpoint = context => RedirectToAuthorizationEndpointAsync(
            context,
            githubOptions.ApiBaseUrl,
            options.CallbackPath);
    }

    private static async Task HandleCreatingTicketAsync(OAuthCreatingTicketContext context)
    {
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        profileRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        profileRequest.Headers.UserAgent.ParseAdd("Automaton-Studio");

        using var profileResponse = await context.Backchannel.SendAsync(profileRequest, context.HttpContext.RequestAborted);
        profileResponse.EnsureSuccessStatusCode();
        using var profileDocument = JsonDocument.Parse(
            await profileResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

        var email = await GitHubOAuthHelpers.GetVerifiedGitHubEmailAsync(context);
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("GitHub account does not expose a verified email.");

        context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email));
        AddProfileClaims(context.Identity, profileDocument.RootElement);
    }

    private static void AddProfileClaims(ClaimsIdentity? identity, JsonElement profile)
    {
        var name = profile.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var login = profile.TryGetProperty("login", out var loginElement)
            ? loginElement.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(login))
        {
            identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, login));
            identity?.AddClaim(new Claim("urn:github:login", login));
        }

        if (!string.IsNullOrWhiteSpace(name))
            identity?.AddClaim(new Claim(ClaimTypes.Name, name));
    }

    private static Task RedirectToAuthorizationEndpointAsync(
        RedirectContext<OAuthOptions> context,
        string? apiBaseUrl,
        PathString callbackPath)
    {
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            var callbackUri = new Uri(new Uri(apiBaseUrl.TrimEnd('/')), callbackPath).ToString();
            context.RedirectUri = GitHubOAuthHelpers.ReplaceQueryStringValue(
                context.RedirectUri,
                "redirect_uri",
                callbackUri);
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
