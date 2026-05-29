using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Studio.Server.Authentication.GitHub;

public static class GitHubAuthenticationServiceCollectionExtensions
{
    public static AuthenticationBuilder AddGitHubOAuth(this AuthenticationBuilder authenticationBuilder, IConfiguration configuration)
    {
        return authenticationBuilder.AddOAuth(GitHubAuthenticationDefaults.Scheme, options =>
        {
            var githubOptions = configuration.GetSection("GitHub").Get<GitHubOAuthOptions>() ?? new GitHubOAuthOptions();

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

            options.Events.OnCreatingTicket = async context =>
            {
                using var profileRequest = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                profileRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                profileRequest.Headers.UserAgent.ParseAdd("Automaton-Studio");

                using var profileResponse = await context.Backchannel.SendAsync(profileRequest, context.HttpContext.RequestAborted);
                profileResponse.EnsureSuccessStatusCode();

                using var profileDocument = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

                var email = await GitHubOAuthHelpers.GetVerifiedGitHubEmailAsync(context);
                if (string.IsNullOrWhiteSpace(email))
                {
                    throw new InvalidOperationException("GitHub account does not expose a verified email.");
                }

                var name = profileDocument.RootElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                var login = profileDocument.RootElement.TryGetProperty("login", out var loginElement)
                    ? loginElement.GetString()
                    : null;

                context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email));

                if (!string.IsNullOrWhiteSpace(login))
                {
                    context.Identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, login));
                    context.Identity?.AddClaim(new Claim("urn:github:login", login));
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name));
                }
            };

            options.Events.OnRedirectToAuthorizationEndpoint = context =>
            {
                if (!string.IsNullOrWhiteSpace(githubOptions.ApiBaseUrl))
                {
                    var callbackUri = new Uri(new Uri(githubOptions.ApiBaseUrl.TrimEnd('/')), options.CallbackPath).ToString();
                    context.RedirectUri = GitHubOAuthHelpers.ReplaceQueryStringValue(context.RedirectUri, "redirect_uri", callbackUri);
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
    }
}
