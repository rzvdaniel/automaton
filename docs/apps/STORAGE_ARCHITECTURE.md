# App Storage Architecture

## Overview

Automaton uses a **hybrid storage architecture** for web apps:

- **Local File System**: Always present as cache/primary storage for users without GitHub
- **GitHub**: Optional source of truth for versioning, collaboration, and backup

## Storage Modes

### 1. LocalOnly Mode (Default)
```
User edits in UI → Local File System → Serve to users
```

- Files stored in `AppData/apps/{userId}/{appId}/`
- No version history (beyond what user manually saves)
- Simple, works offline, no external dependencies

### 2. GitHubPrimary Mode (When GitHub Sync Enabled)
```
User edits in UI → Local Cache → Push to GitHub
                        ↑
GitHub webhook/poll → Pull to Cache → Sync status updated
```

- GitHub repository is the source of truth
- Local file system is a cache for fast serving
- Full version history via Git
- Can edit from VS Code, GitHub.com, or Automaton UI

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    UnifiedAppStorageService                      │
│  (Orchestrates reads/writes based on storage mode)              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─────────────────────┐        ┌─────────────────────┐         │
│  │ AppFileStorageService│        │    GitHubService    │         │
│  │   (Local Cache)      │◄──────►│  (Remote Storage)   │         │
│  └─────────────────────┘        └─────────────────────┘         │
│           │                              │                       │
│           ▼                              ▼                       │
│  ┌─────────────────────┐        ┌─────────────────────┐         │
│  │  Local File System   │        │  GitHub Repository  │         │
│  │  AppData/apps/...    │        │  user/repo-name     │         │
│  └─────────────────────┘        └─────────────────────┘         │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Service Responsibilities

### UnifiedAppStorageService
- Decides which storage provider to use based on app settings
- Orchestrates sync between local and GitHub
- Handles cache invalidation (5-minute TTL by default)
- Manages sync status (Synced, LocalChanges, RemoteChanges, Conflict)

### AppFileStorageService (Local)
- Reads/writes files to local file system
- Security: File extension whitelist, path traversal protection
- Always available, used as cache when GitHub is enabled

### GitHubService
- Push/pull files to/from GitHub repositories
- Create repositories for new apps
- Check sync status against remote

## File Structure

### Local Storage
```
AppData/
└── apps/
    └── {userId}/
        └── {appId}/
            ├── _metadata.json    # App metadata (settings, sync info)
            ├── index.html        # Main HTML file
            ├── styles.css        # Stylesheets
            ├── app.js            # JavaScript
            └── assets/           # Images, fonts, etc.
```

### GitHub Repository
```
{repo}/
└── {app-name}/
    ├── README.md             # Auto-generated app info
    ├── index.html
    ├── styles.css
    ├── app.js
    └── assets/
```

## Security Considerations

### File Extension Whitelist
Only these file types are allowed:
- Web: `.html`, `.css`, `.js`, `.json`, `.xml`
- Images: `.png`, `.jpg`, `.svg`, `.webp`, `.ico`
- Fonts: `.woff`, `.woff2`, `.ttf`
- Other: `.md`, `.txt`, `.map`

**Blocked**: `.ps1`, `.sh`, `.php`, `.exe`, `.bat`, `.cmd`, etc.

### Path Traversal Protection
All file paths are validated to ensure they stay within the app directory.

### Sandboxed Serving (Recommended)
For production, serve user apps from a separate domain:
```
Main app:     app.yoursite.com
User apps:    apps.yoursite-cdn.com/{user}/{app}
```

This prevents XSS attacks from affecting your main application.

## Sync Flow

### Read Operation
```
1. Check storage mode (LocalOnly or GitHubPrimary)
2. If GitHubPrimary:
   a. Check cache validity (last sync time)
   b. If stale (>5 min), pull from GitHub
   c. Update local cache
3. Read from local cache
4. Return content
```

### Write Operation
```
1. Write to local cache immediately
2. If GitHubPrimary:
   a. Push to GitHub
   b. If success: Update sync status to Synced
   c. If fail: Mark as LocalChanges (will retry later)
3. Return success
```

### Manual Sync
Users can trigger manual sync via:
- "Push to GitHub" - Force push local changes
- "Pull from GitHub" - Force pull remote changes
- "Sync" - Bidirectional sync with conflict detection

## API Endpoints

### File Operations
- `GET /api/apps/{id}/files` - List all files
- `GET /api/apps/{id}/files/{path}` - Read file content
- `PUT /api/apps/{id}/files/{path}` - Write file content
- `DELETE /api/apps/{id}/files/{path}` - Delete file

### GitHub Operations
- `POST /api/apps/{id}/github/push` - Push to GitHub
- `POST /api/apps/{id}/github/pull` - Pull from GitHub
- `GET /api/apps/{id}/github/status` - Check sync status
- `PUT /api/apps/{id}/github/settings` - Configure GitHub settings

## Configuration

### Enable GitHub Sync for an App
1. User connects GitHub account (OAuth)
2. User clicks "Enable GitHub Sync" in app settings
3. System creates repository (or user specifies existing)
4. Initial push of all files
5. Sync enabled - all changes push automatically

### Disable GitHub Sync
1. User clicks "Disable GitHub Sync"
2. Local files remain unchanged
3. GitHub repo remains (user can keep or delete manually)
4. App reverts to LocalOnly mode

## Future Enhancements

1. **Webhooks**: GitHub → Automaton webhooks for real-time sync
2. **Conflict Resolution UI**: Visual diff/merge for conflicts
3. **Branch Support**: Deploy from different branches
4. **CDN Integration**: Serve files from CloudFront/Cloudflare
5. **Blob Storage**: Option to use Azure Blob/S3 instead of local files
