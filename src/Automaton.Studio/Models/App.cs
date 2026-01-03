namespace Automaton.Studio.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    // Optional runner to execute flows initiated by this app
    public Guid? ExecutionRunnerId { get; set; }

    // Appearance configuration
    public AppAppearance Appearance { get; set; } = new AppAppearance();

    /// <summary>
    /// Indicates whether a background image is set (derived from Appearance.BackgroundImageUrl).
    /// </summary>
    public bool HasBackgroundImage => !string.IsNullOrEmpty(Appearance?.BackgroundImageUrl);

    public AppDefinition Definition { get; set; } = new AppDefinition();
}

public class AppDefinition
{
    /// <summary>
    /// HTML content for index.html
    /// This is loaded from the file system for editing in the UI
    /// </summary>
    public string? HtmlContent { get; set; }
    
    public IList<string>? ConnectedFlowIds { get; set; } = new List<string>();
    public AppSettings Settings { get; set; } = new AppSettings();
    public GitHubSettings? GitHub { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// GitHub integration settings for an app
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

public class AppListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public string Description { get; set; }

    // Optional ExecutionRunnerId for quick access in UI
    public Guid? ExecutionRunnerId { get; set; }

    // Appearance configuration
    public AppAppearance Appearance { get; set; } = new AppAppearance();

    /// <summary>
    /// Indicates whether a background image is set (derived from Appearance.BackgroundImageUrl).
    /// </summary>
    public bool HasBackgroundImage => !string.IsNullOrEmpty(Appearance?.BackgroundImageUrl);
}

public class NewApp
{
    public string Name { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
}
