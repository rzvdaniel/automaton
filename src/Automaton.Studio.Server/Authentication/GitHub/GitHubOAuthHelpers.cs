using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.WebUtilities;

namespace Automaton.Api.Authentication.GitHub;

public static class GitHubOAuthHelpers
{
    public static async Task<string?> GetVerifiedGitHubEmailAsync(OAuthCreatingTicketContext context)
    {
        using var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        emailRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        emailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        emailRequest.Headers.UserAgent.ParseAdd("Automaton-Studio");

        using var emailResponse = await context.Backchannel.SendAsync(emailRequest, context.HttpContext.RequestAborted);
        emailResponse.EnsureSuccessStatusCode();

        using var emailDocument = JsonDocument.Parse(await emailResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        string? fallbackVerifiedEmail = null;

        foreach (var emailEntry in emailDocument.RootElement.EnumerateArray())
        {
            var isVerified = emailEntry.TryGetProperty("verified", out var verifiedElement) && verifiedElement.GetBoolean();
            if (!isVerified)
            {
                continue;
            }

            if (emailEntry.TryGetProperty("email", out var emailElement))
            {
                var email = emailElement.GetString();
                if (!string.IsNullOrWhiteSpace(email))
                {
                    var isPrimary = emailEntry.TryGetProperty("primary", out var primaryElement) && primaryElement.GetBoolean();
                    if (isPrimary)
                    {
                        return email;
                    }

                    fallbackVerifiedEmail ??= email;
                }
            }
        }

        return fallbackVerifiedEmail;
    }

    public static string ReplaceQueryStringValue(string url, string key, string value)
    {
        var uri = new Uri(url);
        var queryParams = QueryHelpers.ParseQuery(uri.Query)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

        queryParams[key] = value;

        return QueryHelpers.AddQueryString(uri.GetLeftPart(UriPartial.Path), queryParams);
    }
}
