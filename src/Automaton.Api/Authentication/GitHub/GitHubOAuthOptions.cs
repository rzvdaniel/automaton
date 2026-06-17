namespace Automaton.Api.Authentication.GitHub;

public class GitHubOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? ApiBaseUrl { get; set; }
    public string? FrontendUrl { get; set; }
}
