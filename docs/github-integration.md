# GitHub Integration for Apps

## Overview

Automaton now supports GitHub integration for web apps, allowing you to version control, backup, and sync your apps with GitHub repositories. This feature supports both single-file and multi-file app structures.

## Features

### Secure Token Management
- Store GitHub Personal Access Tokens securely (encrypted with ASP.NET Data Protection)
- Per-user token storage
- Token validation and status checking

### Repository Operations
- **Create Repository:** Create a new GitHub repository directly from Automaton
- **Push to GitHub:** Upload your app to a GitHub repository with automatic README generation
- **Pull from GitHub:** Download and sync your app from a GitHub repository
- **Sync Status:** Check if your local app is in sync with the remote repository

### Multi-File Support
The GitHub integration supports both single-file and multi-file app structures:

#### Single-File Apps
- Traditional apps with all HTML/CSS/JS in one file
- Stored as `index.html` in the repository

#### Multi-File Apps
- Organized file structure with separate HTML, CSS, and JavaScript files
- Support for ES modules without build tools
- File structure:
  ```
  app-name/
  ├── index.html          # Entry point
  ├── css/                # Stylesheets
  │   ├── style.css
  │   └── theme.css
  ├── js/                 # JavaScript modules
  │   ├── main.js
  │   ├── utils.js
  │   └── api.js
  ├── assets/             # Static assets (images, JSON, etc.)
  └── README.md           # Auto-generated metadata
  ```

## Getting Started

### 1. Configure GitHub Token

Before using GitHub integration, you need to add a GitHub Personal Access Token:

1. Go to **Settings** → **GitHub** (or your account settings)
2. Create a GitHub Personal Access Token with `repo` permissions
   - Visit: https://github.com/settings/tokens
   - Select scopes: `repo` (full control of private repositories)
3. Copy and paste the token into Automaton
4. Save

### 2. Enable GitHub Sync for an App

1. Open your app in the App Designer
2. Go to the **Settings** tab
3. Scroll to the **GitHub Integration** section
4. Enable **GitHub Sync**
5. Configure your repository settings:
   - **Repository URL:** `username/repo-name` (e.g., `johndoe/my-awesome-app`)
   - **Branch:** The branch to sync with (default: `main`)
   - **Path:** Optional folder path within the repository (e.g., `apps/my-app`)

### 3. Create or Connect to a Repository

#### Option A: Create a New Repository
Click **Create New Repo** to automatically create a new GitHub repository for this app. The repository will be:
- Named based on your app name (lowercased, spaces replaced with hyphens)
- Private by default
- Initialized with your app's content

#### Option B: Use an Existing Repository
Enter the repository URL manually in the format `username/repo-name`.

### 4. Sync Your App

#### Push to GitHub
Click **Push to GitHub** to upload your app to the repository. This will:
- Create/update all app files in the repository
- Generate a README.md with app metadata
- Track the commit SHA for sync status

#### Pull from GitHub
Click **Pull from GitHub** to download the latest version from the repository. This will:
- Overwrite your local app with the GitHub version
- Update the sync status
- Preserve your app's other settings (PWA, appearance, etc.)

#### Check Status
Click **Check Status** to see if your local app is in sync with GitHub:
- **Synced:** Local and remote versions match
- **LocalChanges:** You have unsaved local changes
- **RemoteChanges:** The GitHub version is newer
- **Conflict:** Both local and remote have changes

## API Endpoints

### GitHub Token Management
```
POST   /api/github/token           - Set GitHub token for current user
GET    /api/github/token/status    - Check if user has a token configured
POST   /api/github/token/validate  - Validate the current user's token
DELETE /api/github/token           - Remove GitHub token
```

### Repository Operations
```
POST /api/github/repositories      - Create a new repository
```

### App Sync Operations
```
POST /api/apps/{id}/github/push          - Push app to GitHub
POST /api/apps/{id}/github/pull          - Pull app from GitHub
GET  /api/apps/{id}/github/status        - Check sync status
PUT  /api/apps/{id}/github/settings      - Update GitHub settings for an app
```

## Data Model

### GitHubSettings
```csharp
public class GitHubSettings
{
    public string? RepositoryUrl { get; set; }      // "username/repo-name"
    public string Branch { get; set; }              // default: "main"
    public string? Path { get; set; }               // optional subfolder
    public bool SyncEnabled { get; set; }           // enable/disable sync
    public DateTime? LastSyncedAt { get; set; }     // last successful sync
    public string? LastSyncedCommitSha { get; set; } // last synced commit
    public GitHubSyncStatus SyncStatus { get; set; } // sync status
}
```

### AppFileStructure (Multi-File Apps)
```csharp
public class AppFileStructure
{
    public string IndexHtml { get; set; }                    // Entry point
    public Dictionary<string, string> HtmlFiles { get; set; } // Additional HTML
    public Dictionary<string, string> CssFiles { get; set; }  // Stylesheets
    public Dictionary<string, string> JsFiles { get; set; }   // JavaScript modules
    public Dictionary<string, string> Assets { get; set; }    // Other files
}
```

## Security Considerations

1. **Token Storage:** GitHub tokens are encrypted using ASP.NET Data Protection before being stored in the database
2. **Token Scope:** Users should create tokens with minimal required permissions (`repo` scope)
3. **Private Repositories:** Apps can be stored in private repositories for additional security
4. **Access Control:** Only the app owner can configure GitHub sync settings

## Best Practices

1. **Commit Messages:** Push operations include automatic commit messages like "Update [AppName] from Automaton"
2. **Branch Strategy:** Use a dedicated branch (e.g., `automaton-sync`) if you want to keep manual edits separate
3. **Conflict Resolution:** If conflicts occur, pull the GitHub version first, then make your changes and push again
4. **Regular Syncs:** Push changes regularly to maintain backups
5. **README:** The auto-generated README includes app metadata (version, last updated, etc.)

## Limitations

- **Single Directory:** Each app is stored in a single directory in the repository
- **No Conflict Auto-Merge:** Conflicts must be resolved manually by choosing pull or push
- **No Webhook Support:** Automatic sync from GitHub changes is not yet implemented
- **File Size:** Large assets (images) should be base64-encoded or hosted externally

## Future Enhancements

1. **Multi-File Editor UI:** Visual file tree and multi-tab editor
2. **Visual Diff/Merge:** Compare local and remote versions before sync
3. **Background Sync:** Automatic periodic syncing
4. **Webhook Integration:** Auto-pull when GitHub repository changes
5. **Branch Management:** Create and switch branches from the UI
6. **Collaborative Editing:** Multiple users editing the same app

## Troubleshooting

### "No GitHub token configured"
- Configure a GitHub Personal Access Token in Settings → GitHub

### "Failed to push to GitHub"
- Verify your token has `repo` permissions
- Check that the repository URL is correct
- Ensure the repository exists (or use "Create New Repo")

### "Pull overwrites my changes"
- Pull creates a backup of your current version in the execution history
- You can also push your changes first to save them

### "Invalid repository URL format"
- Use format: `username/repository-name`
- Example: `johndoe/my-web-app`

## Example: Complete Workflow

```bash
# 1. Create an app in Automaton Studio
# 2. Configure GitHub token (one-time setup)
# 3. In App Designer → Settings → GitHub Integration:

Enable GitHub Sync: ON
Repository URL: johndoe/my-portfolio
Branch: main
Path: apps/contact-form

# 4. Click "Create New Repo" (or use existing)
# 5. Make changes to your app
# 6. Click "Push to GitHub"
# 7. Verify on GitHub: https://github.com/johndoe/my-portfolio

# Result: Your app is now version-controlled and backed up!
```

## Related Documentation

- [Apps Overview](apps.md)
- [Multi-File Apps](multi-file-apps.md)
- [ES Modules Support](es-modules.md)
- [Security Best Practices](security.md)
