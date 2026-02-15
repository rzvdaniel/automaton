# App Preview System

## Overview

The app preview system enables live, in-browser previewing of multi-file HTML/CSS/JavaScript apps during editing. Preview reflects unsaved changes in real-time and supports multi-page navigation, external assets (images, fonts), and proper error handling.

**Key Difference from Published Apps:**
- Published apps are served from persistent storage (disk/cloud)
- Preview apps are served from in-memory cache + disk fallback, allowing unsaved edits to be previewed immediately

## Problem Statement

The original preview implementation used iframe `document.write()`, which has critical limitations:

- ❌ Cannot handle multiple files (only inline content)
- ❌ No support for external CSS/JS files
- ❌ Cannot handle modules or modern JS patterns
- ❌ Form submissions reload the iframe, breaking UX
- ❌ No error visibility for missing assets

The HTTP-based preview system solves all of these by serving preview content from a dedicated endpoint, just like published apps.

## Architecture

### System Components

```
┌─────────────────────────────────────────────────────────┐
│                    Frontend (Vue.js)                    │
│  ┌────────────────┐  ┌──────────────────┐              │
│  │  AppEditorView │  │ PreviewOverlay   │              │
│  │   (component)  │  │  (full-screen)   │              │
│  └────────┬───────┘  └────────┬─────────┘              │
│           │                    │                        │
│           └────────┬───────────┘                        │
│                    │                                    │
│          ┌─────────▼──────────┐                       │
│          │ app-editor.store   │                       │
│          │  • previewUrl      │                       │
│          │  • previewWarning  │                       │
│          │  • buildPreview()  │                       │
│          │  • refreshPreview()│                       │
│          └─────────┬──────────┘                       │
│                    │                                    │
│          ┌─────────▼──────────┐                       │
│          │ apps.service.ts    │                       │
│          │  • uploadPreview() │                       │
│          │  • getFiles()      │                       │
│          │  • readFile()      │                       │
│          └─────────┬──────────┘                       │
└────────────────────┼────────────────────────────────────┘
                     │ HTTP
         ┌───────────▼───────────┐
         │  AppPreviewController │
         │  ✓ AllowAnonymous     │
         └───────────┬───────────┘
                     │
      ┌──────────────┴──────────────┐
      │                             │
┌─────▼──────────────┐   ┌──────────▼─────────────┐
│ AppPreviewStorage  │   │ AppFileStorageService  │
│  • In-memory cache │   │  • Persistent storage  │
│  • 30-min TTL      │   │  • Disk/Cloud files    │
│  • Auto-cleanup    │   │                        │
└────────────────────┘   └────────────────────────┘
```

## Data Flow

### 1. Preview Refresh (User clicks "Refresh Preview")

```typescript
// Frontend: app-editor.store.ts
await refreshPreview()
  ├─ buildPreviewFiles()              // Collect all files to preview
  │  ├─ getFiles() API                // Get file list from server
  │  ├─ readFile() API                // Read persisted files
  │  └─ Collect dirty editor buffers  // Include unsaved changes
  │
  ├─ uploadPreviewContent()           // Upload to preview endpoint
  │  ├─ POST /api/apps/{appId}/preview
  │  ├─ Send: { html, files: {...} }
  │  └─ Backend stores in memory
  │
  └─ setPreviewUrl()                  // Set iframe.src to preview endpoint
     └─ iframe loads index.html from /app-preview/{userId}/{appId}/
```

### 2. Preview File Serving

```
Browser requests: GET /app-preview/{userId}/{appId}/styles/main.css

AppPreviewController.ServePreviewFile()
├─ Lookup in previewStorage (in-memory cache)
│  └─ Returns immediately if found (unsaved changes)
│
└─ If not in cache:
   └─ Lookup in fileStorage (persistent storage)
      └─ Load from disk/cloud
      └─ Return with no-cache headers
```

### 3. Error Detection & Display

```javascript
// Backend injects error listener into preview HTML script tag
window.addEventListener('error', (e) => {
  if (e.filename.includes('app-preview')) {
    parent.postMessage({
      type: 'previewResourceError',
      url: e.filename,
      tagName: 'script'
    }, '*')
  }
})

// Frontend receives error event
window.addEventListener('message', (e) => {
  if (e.data.type === 'previewResourceError') {
    editorStore.setPreviewWarning(e.data.url)  // Show banner
  }
})
```

## Backend Components

### AppPreviewStorageService

**Purpose:** In-memory cache for live preview files

**Key Features:**
- `StorePreviewContent(userId, appId, html, files)` - Store preview files
- `GetPreviewFile(userId, appId, path)` - Retrieve cached file
- Automatic TTL (30 minutes) with cleanup timer
- Path normalization (Windows `\` → Unix `/`)

**Implementation:**
```csharp
// Stores files in memory with expiration
_cache[key] = new PreviewContent {
    Html = html,
    Files = files ?? new(),
    ExpiresAt = DateTime.UtcNow.AddMinutes(30)
}

// Cleanup timer runs every 5 minutes
_cleanupTimer.Elapsed += async (s, e) => {
    // Remove expired entries
}
```

**Scalability:**
- Dictionary lookup is O(1)
- Memory scales with file count (typical app: 10-100 files, <5MB)
- For very large apps (>1000 files), entries expire after 30 min

### AppPreviewController

**Endpoints:**

1. **Store Preview Content** (Protected)
   ```
   POST /api/apps/{appId}/preview
   Body: { html: string, files?: Dictionary<string, string> }
   ```
   - Resolves app owner from database
   - Stores files in `previewStorage`
   - Returns `{ success: true }`

2. **Serve Preview File** (Public)
   ```
   GET /app-preview/{userId}/{appId}/{*path}
   ```
   - Example: `/app-preview/550e8400-e29b-41d4-a716-446655440000/app-123/styles/main.css`
   - Returns file with correct MIME type
   - Adds security headers (`X-Content-Type-Options: nosniff`)
   - No-cache headers (always fresh for preview)

**Security:**
- Serve endpoint is `[AllowAnonymous]` but requires knowing exact `userId` and `appId` UGUIDs
- GUIDs are cryptographically random (4.6e38 possible combinations)
- Not a substitute for auth, but sufficient for preview-during-edit use case
- Preview files expire after 30 minutes of inactivity

## Frontend Components

### app-editor.store.ts (Pinia)

**State:**
```typescript
previewUrl: string | null       // Current preview iframe src
previewWarning: string | null   // Missing asset URL or error message
previewLoading: boolean         // Upload in progress
```

**Actions:**
```typescript
async refreshPreview() {
  // 1. Collect files to upload
  const files = await buildPreviewFiles()
  
  // 2. Upload to backend
  await appsService.uploadPreviewContent(appId, html, files)
  
  // 3. Generate URL with cache-busting timestamp
  const timestamp = Date.now()
  setPreviewUrl(`/app-preview/${userId}/${appId}/?v=${timestamp}`)
}

setPreviewWarning(message: string | null) {
  previewWarning = message
}
```

**File Collection Logic:**
```typescript
async buildPreviewFiles() {
  const files: Record<string, string> = {}
  
  // Get persisted files from server
  const fileList = await appsService.getFiles(appId)
  for (const file of fileList) {
    if (isDirty(file.path)) {
      files[file.path] = editorStore.getEditorContent(file.path)
    } else {
      files[file.path] = await appsService.readFile(appId, file.path)
    }
  }
  
  return files
}
```

### PreviewOverlay.vue

**Core Template:**
```vue
<template>
  <div class="preview-overlay">
    <!-- Error/Warning Banner -->
    <div v-if="editorStore.previewWarning" class="warning-banner">
      <div class="warning-content">
        <span>⚠️ Missing asset detected:</span>
        <code>{{ editorStore.previewWarning }}</code>
      </div>
    </div>

    <!-- Preview Iframe -->
    <iframe 
      :src="editorStore.previewUrl"
      :key="editorStore.previewUrl"
      class="preview-frame"
      sandbox="allow-same-origin allow-scripts allow-forms allow-popups"
    ></iframe>
  </div>
</template>
```

**Sandbox Attributes:**
- `allow-same-origin` - Allows access to preview endpoint files
- `allow-scripts` - Executes JavaScript in preview
- `allow-forms` - Allows form submission
- `allow-popups` - Allows window.open()

### apps.service.ts

**Upload Method:**
```typescript
async uploadPreviewContent(
  appId: string,
  html: string,
  files?: Record<string, string>
) {
  // Inject error listener into HTML before upload
  const enrichedHtml = injectErrorListener(html)
  
  return axios.post(`/api/apps/${appId}/preview`, {
    html: enrichedHtml,
    files
  })
}

function injectErrorListener(html: string) {
  const script = `
    window.addEventListener('error', (e) => {
      if (e.filename?.includes('app-preview')) {
        parent.postMessage({
          type: 'previewResourceError',
          url: e.filename,
          tagName: e.target?.tagName || 'unknown'
        }, '*')
      }
    })
  `
  
  // Insert before closing </body> tag
  return html.replace('</body>', `<script>${script}</script></body>`)
}
```

## Multi-File & Multi-Page Support

### How Navigation Works

When a user clicks a link like `<a href="about.html">`:

1. Browser navigates within iframe: `location.href = "about.html"`
2. Browser requests: `GET /app-preview/{userId}/{appId}/about.html`
3. Controller serves `about.html` from memory/disk (same as index.html)
4. Page loads and executes normally

**No special handling required.** Each HTML file is independently served from the preview endpoint.

### Relative Path Resolution

Preview endpoint correctly resolves relative paths:
```
GET /app-preview/{userId}/{appId}/pages/about.html
  └─ link href="../styles/shared.css"
  └─ resolves to: /app-preview/{userId}/{appId}/styles/shared.css ✓

GET /app-preview/{userId}/{appId}/index.html
  └─ link href="./styles/main.css"
  └─ resolves to: /app-preview/{userId}/{appId}/styles/main.css ✓
```

## Scalability Considerations

### File Count & Memory Usage

| Scenario | Memory | Notes |
|----------|--------|-------|
| Small app (10 files, 1MB) | ~1MB | Typical use case |
| Medium app (50 files, 5MB) | ~5MB | Multiple pages + assets |
| Large app (200+ files, 20MB) | 20MB+ | May need optimization |

### Optimization: Diff-Based Upload

For very large apps, only upload changed files:

```typescript
// Future optimization
async uploadPreviewDiff() {
  const dirty = await buildDirtyFiles()  // Only unsaved files
  const changed = await getChangedFiles()  // Git-like diff
  
  await appsService.uploadPreviewContent(appId, html, {
    ...dirty,
    ...changed
  })
  
  // Backend serves everything from disk for missing files
  // Rather than requiring all files in memory
}
```

**Benefits:**
- Reduces upload bandwidth for large apps
- Reduces memory usage (only live edits in cache)
- Disk fallback handles persisted files

### TTL & Cleanup

- **Cache duration:** 30 minutes of inactivity
- **Cleanup interval:** Every 5 minutes
- **Behavior:** Expired entries removed automatically
- **Use case:** Prevents memory leaks from abandoned preview sessions

## Error Handling

### Missing Asset Detection

**When an asset fails to load:**

1. Browser fires `error` event to window
2. Injected listener captures URL and tags name
3. postMessage sends error to parent frame
4. Frontend shows warning banner with asset URL

**User can then:**
- Create the missing file (quick action button in banner)
- Check file paths in resource tree
- Re-upload preview

### Network Errors

- 404 responses show styled error page
- 500 errors logged on backend, user sees generic message
- No-cache headers prevent stale content from causing confusion

## Security & Privacy

### Authentication

- **Store endpoint** (`POST /api/apps/{appId}/preview`): `[Authorize]`
  - Only authenticated users can upload preview files
  - User must have access to the app

- **Serve endpoint** (`GET /app-preview/...`): `[AllowAnonymous]`
  - No authentication required
  - Protected by GUID randomness (not a secret, but obscure)
  - Intentionally not behind auth (enables link sharing during collaboration)

### Headers & Protections

```csharp
Response.Headers.XContentTypeOptions = "nosniff";      // Prevent MIME-type sniffing
Response.Headers.CacheControl = "no-cache, no-store";  // No caching
Response.Headers.Pragma = "no-cache";                  // Legacy cache control
Response.Headers.Expires = "0";                        // Already expired
```

### Iframe Sandbox

```html
<iframe sandbox="allow-same-origin allow-scripts allow-forms allow-popups"></iframe>
```

- Prevents top-window navigation (`allow-top-navigation` not set)
- Prevents plugins and Java
- Allows forms for Web App testing
- Allows popups for user interactions

## Comparison: Preview vs Published vs Draft

| Feature | Preview (Live Edit) | Published | Draft (Persisted) |
|---------|-------------------|-----------|-------------------|
| Location | In-memory + disk | Persistent storage | Persistent storage |
| Reflects unsaved changes | ✅ Yes | ❌ No | ❌ No |
| Multi-file support | ✅ Yes | ✅ Yes | ✅ Yes |
| External assets | ✅ CSS, JS, images, fonts | ✅ All asset types | ✅ All asset types |
| Accessible without editor | ❌ No | ✅ Yes | ❌ No |
| Public URL | ❌ No | ✅ Yes (`/web/{publicUrl}`) | ❌ No |
| Expires | ✅ Yes (30 min TTL) | ❌ No | ❌ No |
| Cache behavior | No-cache (always fresh) | Cache-friendly | N/A |

## Development & Debugging

### Viewing Preview Cache Contents

Backend logs file storage operations:
```
[INFO] Stored preview content for app {AppId} (user {UserId})
[WARN] Preview file not found: {Path} for app {AppId}
```

### Testing Multi-File Preview

1. Create an app with multiple HTML files
2. Add CSS and JS files with relative imports
3. Use preview to navigate between pages
4. Check browser DevTools for 404 errors
5. Verify warning banner appears if assets are missing

### Local Development

```bash
# Backend
cd src/Automaton.Studio.Server
dotnet run

# Frontend
cd src/automaton-studio-vue
npm run dev

# Test preview at
# http://localhost:5173 → editor
# http://localhost:7091/app-preview/{userId}/{appId}/
```

## Future Enhancements

- [ ] **Diff-based upload** - Only send changed files for large apps
- [ ] **Quick file creation** - "Create missing file" button in warning banner
- [ ] **Preview persistence** - Option to keep preview cache longer than 30 min
- [ ] **Asset size warnings** - Alert user if preview bundle exceeds memory threshold
- [ ] **Live reload** - Auto-refresh preview when file changes detected
- [ ] **Preview history** - Revert to previous preview snapshots

## Related Documentation

- [Apps Platform](../apps/DEPLOYMENT.md) - Publishing and deployment
- [Web App Integration](../skills/web-app-integration/SKILL.md) - Calling flows from apps
- [Runners](../runners/) - Executing flows triggered from apps
