using Octokit;

namespace Automaton.Api.Services;

/// <summary>
/// Provides account-level GitHub repository operations used by Studio.
/// App source synchronization is intentionally handled through the local CLI workflow.
/// </summary>
public class GitHubService
{
    private readonly Serilog.ILogger logger;
    private readonly UserContextService userContextService;
    private readonly GitHubTokenService tokenService;

    public GitHubService(
        UserContextService userContextService,
        GitHubTokenService tokenService)
    {
        this.userContextService = userContextService;
        this.tokenService = tokenService;
        this.logger = Serilog.Log.ForContext<GitHubService>();
    }

    /// <summary>
    /// Create a GitHub client for the current user.
    /// </summary>
    private async Task<GitHubClient> CreateClientAsync()
    {
        var token = await tokenService.GetTokenForUserAsync(userContextService.GetUserId());
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("No GitHub token configured. Please add a GitHub token in settings.");
        }

        return new GitHubClient(new ProductHeaderValue("Automaton"))
        {
            Credentials = new Credentials(token)
        };
    }

    /// <summary>
    /// Create a new repository for an app.
    /// </summary>
    public async Task<string> CreateRepositoryAsync(string appName, string description, bool isPrivate = true)
    {
        var client = await CreateClientAsync();
        var user = await client.User.Current();

        var repoName = appName.ToLowerInvariant().Replace(" ", "-");
        var newRepo = new NewRepository(repoName)
        {
            Description = description,
            Private = isPrivate,
            AutoInit = true
        };

        try
        {
            var repo = await client.Repository.Create(newRepo);
            logger.Debug("Created GitHub repository: {Owner}/{Repo}", user.Login, repoName);
            return $"{user.Login}/{repoName}";
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create GitHub repository");
            throw;
        }
    }
}
