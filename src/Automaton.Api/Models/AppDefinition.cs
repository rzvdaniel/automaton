using Automaton.Core.Models;

namespace Automaton.Api.Models;

public class AppDefinition
{
    /// <summary>
    /// Compatibility field mirrored from source/index.html when reading or creating an app.
    /// Local app source remains authoritative and is managed through the CLI workflow.
    /// </summary>
    public string? HtmlContent { get; set; }
    
    public IList<string>? ConnectedFlowIds { get; set; } = new List<string>();
    public AppSettings Settings { get; set; } = new AppSettings();
    public GitHubSettings? GitHub { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// Legacy per-app GitHub sync metadata retained for compatibility with existing app records.
/// </summary>
public class GitHubSettings
{
    /// <summary>
    /// GitHub repository URL (e.g., "username/repo-name")
    /// </summary>
    public string? RepositoryUrl { get; set; }
    
    /// <summary>
    /// Branch to sync with (default: "main")
    /// </summary>
    public string Branch { get; set; } = "main";
    
    /// <summary>
    /// Path within repository where app files are stored
    /// </summary>
    public string? Path { get; set; }
    
    /// <summary>
    /// Whether GitHub sync is enabled for this app
    /// </summary>
    public bool SyncEnabled { get; set; } = false;
    
    /// <summary>
    /// Last successful sync timestamp
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
    
    /// <summary>
    /// Last commit SHA that was synced
    /// </summary>
    public string? LastSyncedCommitSha { get; set; }
    
    /// <summary>
    /// Sync status (None, Synced, LocalChanges, RemoteChanges, Conflict)
    /// </summary>
    public GitHubSyncStatus SyncStatus { get; set; } = GitHubSyncStatus.None;
}

public enum GitHubSyncStatus
{
    None = 0,
    Synced = 1,
    LocalChanges = 2,
    RemoteChanges = 3,
    Conflict = 4,
    Error = 5
}

public class AppSettings
{
    public bool EnablePwa { get; set; } = false;
    public string? PwaName { get; set; }
    public string? PwaShortName { get; set; }
    public string? PwaIconUrl { get; set; }
    public string? ThemeColor { get; set; }
    public string? BackgroundColor { get; set; }
}
