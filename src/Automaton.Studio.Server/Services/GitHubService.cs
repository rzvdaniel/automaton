using Octokit;
using System.Text;
using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing GitHub repository operations for apps
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
    /// Create a GitHub client for the current user
    /// </summary>
    private async Task<GitHubClient> CreateClientAsync()
    {
        var token = await tokenService.GetTokenForUserAsync(userContextService.GetUserId());
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("No GitHub token configured. Please add a GitHub token in settings.");
        }

        var client = new GitHubClient(new ProductHeaderValue("Automaton"))
        {
            Credentials = new Credentials(token)
        };

        return client;
    }

    /// <summary>
    /// Parse repository owner and name from URL (e.g., "username/repo-name")
    /// </summary>
    private (string owner, string name) ParseRepositoryUrl(string repositoryUrl)
    {
        var parts = repositoryUrl.Trim('/').Split('/');
        if (parts.Length < 2)
        {
            throw new ArgumentException("Invalid repository URL format. Expected: 'username/repo-name'");
        }

        return (parts[^2], parts[^1]);
    }

    /// <summary>
    /// Push app files to GitHub repository
    /// </summary>
    public async Task<string> PushToGitHubAsync(AppModel app, string commitMessage)
    {
        if (app.Definition.GitHub == null || string.IsNullOrEmpty(app.Definition.GitHub.RepositoryUrl))
        {
            throw new InvalidOperationException("App is not connected to a GitHub repository");
        }

        var client = await CreateClientAsync();
        var (owner, repoName) = ParseRepositoryUrl(app.Definition.GitHub.RepositoryUrl);
        var branch = app.Definition.GitHub.Branch ?? "main";
        var basePath = app.Definition.GitHub.Path ?? app.Name.ToLowerInvariant().Replace(" ", "-");

        try
        {
            // Get the reference to the branch
            var reference = await client.Git.Reference.Get(owner, repoName, $"heads/{branch}");
            var latestCommit = await client.Git.Commit.Get(owner, repoName, reference.Object.Sha);
            var baseTreeSha = latestCommit.Tree.Sha;

            // Create tree items for all files
            var treeItems = new List<NewTreeItem>();

            if (app.Definition.IsMultiFile && app.Definition.Files != null)
            {
                // Multi-file app: push all files
                AddFileToTree(treeItems, $"{basePath}/index.html", app.Definition.Files.IndexHtml);

                foreach (var (fileName, content) in app.Definition.Files.HtmlFiles)
                {
                    AddFileToTree(treeItems, $"{basePath}/{fileName}", content);
                }

                foreach (var (fileName, content) in app.Definition.Files.CssFiles)
                {
                    AddFileToTree(treeItems, $"{basePath}/css/{fileName}", content);
                }

                foreach (var (fileName, content) in app.Definition.Files.JsFiles)
                {
                    AddFileToTree(treeItems, $"{basePath}/js/{fileName}", content);
                }

                foreach (var (fileName, content) in app.Definition.Files.Assets)
                {
                    AddFileToTree(treeItems, $"{basePath}/assets/{fileName}", content);
                }
            }
            else
            {
                // Single-file app: push as index.html
                AddFileToTree(treeItems, $"{basePath}/index.html", app.Definition.HtmlContent ?? "");
            }

            // Add a README with app metadata
            var readme = GenerateReadme(app);
            AddFileToTree(treeItems, $"{basePath}/README.md", readme);

            // Create new tree
            var newTree = new NewTree { BaseTree = baseTreeSha };
            foreach (var item in treeItems)
            {
                newTree.Tree.Add(item);
            }

            var tree = await client.Git.Tree.Create(owner, repoName, newTree);

            // Create commit
            var newCommit = new NewCommit(commitMessage, tree.Sha, reference.Object.Sha);
            var commit = await client.Git.Commit.Create(owner, repoName, newCommit);

            // Update reference
            await client.Git.Reference.Update(owner, repoName, $"heads/{branch}", new ReferenceUpdate(commit.Sha));

            logger.Information("Pushed app {AppName} to GitHub: {Owner}/{Repo} (commit: {Sha})",
                app.Name, owner, repoName, commit.Sha);

            return commit.Sha;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to push app {AppName} to GitHub", app.Name);
            throw;
        }
    }

    /// <summary>
    /// Pull app files from GitHub repository
    /// </summary>
    public async Task<AppDefinition> PullFromGitHubAsync(GitHubSettings githubSettings)
    {
        if (string.IsNullOrEmpty(githubSettings.RepositoryUrl))
        {
            throw new InvalidOperationException("Repository URL is required");
        }

        var client = await CreateClientAsync();
        var (owner, repoName) = ParseRepositoryUrl(githubSettings.RepositoryUrl);
        var branch = githubSettings.Branch ?? "main";
        var basePath = githubSettings.Path ?? "";

        try
        {
            // Get contents of the directory
            var contents = await client.Repository.Content.GetAllContents(owner, repoName, basePath);

            var definition = new AppDefinition
            {
                IsMultiFile = false,
                GitHub = githubSettings
            };

            // Check if it's a multi-file app
            var hasMultipleFiles = contents.Count(c => c.Type == ContentType.File && 
                (c.Name.EndsWith(".html") || c.Name.EndsWith(".js") || c.Name.EndsWith(".css"))) > 1;

            if (hasMultipleFiles)
            {
                definition.IsMultiFile = true;
                definition.Files = new AppFileStructure();

                foreach (var item in contents)
                {
                    if (item.Type != ContentType.File) continue;

                    var content = await GetFileContentAsync(client, owner, repoName, item.Path, branch);

                    if (item.Name == "index.html")
                    {
                        definition.Files.IndexHtml = content;
                    }
                    else if (item.Name.EndsWith(".html"))
                    {
                        definition.Files.HtmlFiles[item.Name] = content;
                    }
                    else if (item.Name.EndsWith(".css"))
                    {
                        definition.Files.CssFiles[item.Name] = content;
                    }
                    else if (item.Name.EndsWith(".js"))
                    {
                        definition.Files.JsFiles[item.Name] = content;
                    }
                    else if (item.Name != "README.md")
                    {
                        definition.Files.Assets[item.Name] = content;
                    }
                }
            }
            else
            {
                // Single file app
                var indexFile = contents.FirstOrDefault(c => c.Name == "index.html");
                if (indexFile != null)
                {
                    definition.HtmlContent = await GetFileContentAsync(client, owner, repoName, indexFile.Path, branch);
                }
            }

            // Get latest commit SHA for sync tracking
            var reference = await client.Git.Reference.Get(owner, repoName, $"heads/{branch}");
            definition.GitHub.LastSyncedCommitSha = reference.Object.Sha;
            definition.GitHub.LastSyncedAt = DateTime.UtcNow;
            definition.GitHub.SyncStatus = GitHubSyncStatus.Synced;

            logger.Information("Pulled app from GitHub: {Owner}/{Repo} at {Path}",
                owner, repoName, basePath);

            return definition;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to pull app from GitHub: {Owner}/{Repo}", owner, repoName);
            throw;
        }
    }

    /// <summary>
    /// Check sync status between local and remote
    /// </summary>
    public async Task<GitHubSyncStatus> CheckSyncStatusAsync(GitHubSettings githubSettings)
    {
        if (string.IsNullOrEmpty(githubSettings.RepositoryUrl))
        {
            return GitHubSyncStatus.None;
        }

        try
        {
            var client = await CreateClientAsync();
            var (owner, repoName) = ParseRepositoryUrl(githubSettings.RepositoryUrl);
            var branch = githubSettings.Branch ?? "main";

            var reference = await client.Git.Reference.Get(owner, repoName, $"heads/{branch}");
            var latestSha = reference.Object.Sha;

            if (string.IsNullOrEmpty(githubSettings.LastSyncedCommitSha))
            {
                return GitHubSyncStatus.RemoteChanges;
            }

            if (latestSha == githubSettings.LastSyncedCommitSha)
            {
                return GitHubSyncStatus.Synced;
            }

            return GitHubSyncStatus.RemoteChanges;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to check sync status");
            return GitHubSyncStatus.Error;
        }
    }

    /// <summary>
    /// Create a new repository for an app
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
            logger.Information("Created GitHub repository: {Owner}/{Repo}", user.Login, repoName);
            return $"{user.Login}/{repoName}";
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to create GitHub repository");
            throw;
        }
    }

    private void AddFileToTree(List<NewTreeItem> treeItems, string path, string content)
    {
        var blob = new NewBlob
        {
            Content = content,
            Encoding = EncodingType.Utf8
        };

        treeItems.Add(new NewTreeItem
        {
            Path = path,
            Mode = "100644", // Regular file
            Type = TreeType.Blob,
            Content = content
        });
    }

    private async Task<string> GetFileContentAsync(GitHubClient client, string owner, string repo, string path, string reference)
    {
        var contents = await client.Repository.Content.GetAllContents(owner, repo, path);
        var file = contents.FirstOrDefault();
        return file?.Content ?? "";
    }

    private string GenerateReadme(AppModel app)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {app.DisplayName ?? app.Name}");
        sb.AppendLine();
        sb.AppendLine(app.Description ?? "Web app created with Automaton");
        sb.AppendLine();
        sb.AppendLine("## App Information");
        sb.AppendLine($"- **Created**: {app.Created:yyyy-MM-dd}");
        sb.AppendLine($"- **Last Updated**: {app.Updated:yyyy-MM-dd}");
        sb.AppendLine($"- **Version**: {app.Version}");
        
        if (app.Definition.IsMultiFile)
        {
            sb.AppendLine();
            sb.AppendLine("## File Structure");
            sb.AppendLine("This is a multi-file web app with the following structure:");
            sb.AppendLine("- `index.html` - Main entry point");
            if (app.Definition.Files?.CssFiles.Any() == true)
            {
                sb.AppendLine("- `css/` - Stylesheets");
            }
            if (app.Definition.Files?.JsFiles.Any() == true)
            {
                sb.AppendLine("- `js/` - JavaScript modules (ES modules supported)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Managed by Automaton - https://github.com/rzvdaniel/automaton*");

        return sb.ToString();
    }
}
