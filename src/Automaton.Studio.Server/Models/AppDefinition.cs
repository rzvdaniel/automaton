using Automaton.Core.Models;

namespace Automaton.Studio.Server.Models;

public class AppDefinition
{
    /// <summary>
    /// Legacy single-file HTML content (for backward compatibility)
    /// </summary>
    public string? HtmlContent { get; set; }
    
    /// <summary>
    /// Multi-file structure for apps with separate HTML/CSS/JS files
    /// </summary>
    public AppFileStructure? Files { get; set; }
    
    /// <summary>
    /// Indicates whether this app uses multi-file structure
    /// </summary>
    public bool IsMultiFile { get; set; } = false;
    
    public IList<string>? ConnectedFlowIds { get; set; } = new List<string>();
    public AppSettings Settings { get; set; } = new AppSettings();
    public GitHubSettings? GitHub { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// File structure for multi-file apps
/// </summary>
public class AppFileStructure
{
    /// <summary>
    /// Entry point HTML file
    /// </summary>
    public string IndexHtml { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional HTML files
    /// </summary>
    public Dictionary<string, string> HtmlFiles { get; set; } = new();
    
    /// <summary>
    /// CSS files
    /// </summary>
    public Dictionary<string, string> CssFiles { get; set; } = new();
    
    /// <summary>
    /// JavaScript files (supports ES modules)
    /// </summary>
    public Dictionary<string, string> JsFiles { get; set; } = new();
    
    /// <summary>
    /// Other static assets (JSON, images as base64, etc.)
    /// </summary>
    public Dictionary<string, string> Assets { get; set; } = new();
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
