# App Versioning & AI Context System

## Overview

Implement a comprehensive versioning system for Apps that captures file changes, step changes, and AI-generated summaries. The system creates immutable snapshots on first change per editing session, enabling rollback and providing persistent AI context via per-app AGENTS.md files.

**Key Principles:**
- AI-first UX: No diff tools needed; AI-generated summaries describe changes
- Session-based versioning: Create version when new AI chat session starts (captures previous session's work)
- Unified versioning: App version = files snapshot (disk) + steps snapshot (database)
- Local storage: All versions stored on server file system (no Git dependency)
- Simplified model: Files on disk are self-documenting; only steps need DB versioning
- Leverages existing infrastructure: Uses existing `AiChatSession` entity

---

## Data Model

### Storage Strategy

| Content | Storage | Rationale |
|---------|---------|-----------|
| App files (HTML/CSS/JS) | Disk only | Complete snapshots in `versions/vNNN/files/` |
| File change metadata | JSON in `AppVersion` | Lightweight, no joins needed |
| Step snapshots | Database table | Steps live in DB, need relational tracking |

### Database Entities

#### `AppVersion`

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid | Primary key |
| `AppId` | Guid | FK to WebApp |
| `VersionNumber` | int | Sequential (1, 2, 3...) |
| `Summary` | string | AI-generated changelog |
| `Trigger` | enum | `NewSession`, `ManualSave`, `Publish`, `Restore` |
| `CreatedAt` | DateTime | Timestamp |
| `CreatedBy` | string | UserId |
| `AiSessionId` | string? | Chat session that triggered version |
| `AiModel` | string? | Model used (e.g., "claude-sonnet-4-20250514") |
| `SnapshotPath` | string | Relative path to version folder |
| `FilesChangeSummary` | string? | JSON array of file changes |

**`FilesChangeSummary` JSON format:**
```json
[
  { "path": "index.html", "action": "Modified", "description": "Added login form" },
  { "path": "auth.js", "action": "Created", "description": "New authentication module" }
]
```

#### `AppVersionStep`

| Column | Type | Description |
|--------|------|-------------|
| `Id` | Guid | Primary key |
| `AppVersionId` | Guid | FK to AppVersion |
| `StepId` | Guid | FK to CustomStep |
| `StepName` | string | Step name at time of snapshot |
| `Action` | enum | `Created`, `Modified`, `Deleted`, `Linked`, `Unlinked` |
| `Description` | string? | AI-generated description of step change |
| `CodeSnapshot` | string | Full step code at time of snapshot |
| `VariablesSnapshot` | string | JSON of step I/O variables at time of snapshot |

### File System Structure

```
AppData/apps/{userId}/{appId}/
├── current/                    # Active working files
│   ├── index.html
│   ├── styles.css
│   ├── app.js
│   └── _metadata.json
├── versions/                   # Immutable snapshots
│   ├── v001/
│   │   └── files/              # Complete copy of all app files
│   │       ├── index.html
│   │       ├── styles.css
│   │       └── _metadata.json
│   ├── v002/
│   │   └── files/
│   │       └── ...
│   └── v003/
│       └── files/
│           └── ...
└── AGENTS.md                   # Persistent AI context
```

**Note:** Step snapshots are stored in `AppVersionStep` table, not on disk. The file system only contains app files.

---

## Session & Version Triggers

### Leveraging Existing AiChatSession

The system uses the existing `AiChatSession` entity to trigger version creation. This is the simplest approach with minimal new infrastructure.

**Existing `AiChatSession` entity:**
```csharp
public class AiChatSession
{
    public Guid Id { get; set; }
    public ChatResourceType ResourceType { get; set; }  // WebApp, Step
    public Guid ResourceId { get; set; }                // App ID
    public string Title { get; set; }
    public string? Provider { get; set; }               // OpenAI, Anthropic, etc.
    public string? Model { get; set; }                  // gpt-4, claude-3, etc.
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Version Trigger: New Session Creation

**Simple rule:** When a new `AiChatSession` is created for a WebApp → snapshot the current state BEFORE any new changes.

This captures all work from the previous session.

### Version Creation Flow

```
User opens App Editor (Day 1)
├── Creates new AiChatSession (or loads existing active one)
│   └── IF new session created → CREATE VERSION v1 (initial state)
├── AI makes changes via MCP tools
│   ├── save_app_code → updates index.html
│   ├── create_backend_step → creates auth step
│   └── ... more changes ...
└── User closes editor

User opens App Editor (Day 2)
├── Creates new AiChatSession
│   └── CREATE VERSION v2 (captures Day 1's work)
├── AI makes more changes
└── User closes editor

User opens App Editor (Day 3)
├── Creates new AiChatSession  
│   └── CREATE VERSION v3 (captures Day 2's work)
└── ... and so on
```

### Additional Triggers

| Trigger | When | Description |
|---------|------|-------------|
| **New Session** | `AiChatSession` created | Automatic - captures previous session's work |
| **Manual Save** | User clicks Save button | Explicit checkpoint |
| **Publish** | User publishes app | Marks a release point |
| **Restore** | User restores a version | Creates snapshot before restore |

### Integration with AiChatHistoryService

Modify `CreateSessionAsync` in `AiChatHistoryService` to trigger version creation:

```csharp
public async Task<ChatSessionDto> CreateSessionAsync(
    ChatResourceType resourceType,
    Guid resourceId,
    string userId,
    CreateChatSessionRequest request)
{
    // If this is a WebApp, create a version snapshot first
    if (resourceType == ChatResourceType.WebApp)
    {
        await appVersionService.CreateSnapshotAsync(
            resourceId, 
            userId, 
            AppVersionTrigger.NewSession,
            sessionId: null  // Will be set after session is created
        );
    }
    
    // Existing session creation logic...
    var sessionEntity = new AiChatSession { ... };
    // ...
}
```

### Benefits of This Approach

1. **Zero configuration** — No thresholds to tune
2. **Natural boundaries** — Sessions map to user intent ("I'm starting fresh")
3. **Existing infrastructure** — Uses `AiChatSession` already in place
4. **Version-to-session linkage** — Each version tied to a session for history tracing
5. **Simple mental model** — "New conversation = new checkpoint"

---

## Implementation Steps

### Phase 1: Database & Storage Foundation

1. **Create EF Core entities** in `src/Automaton.Studio.Server/Entities/`:
   - `AppVersion.cs`
   - `AppVersionStep.cs`
   - `AppVersionTrigger.cs` (enum)
   - `AppVersionStepAction.cs` (enum)

2. **Add DbSets** to `AutomatonDbContext`:
   ```csharp
   public DbSet<AppVersion> AppVersions { get; set; }
   public DbSet<AppVersionStep> AppVersionSteps { get; set; }
   ```

3. **Generate migration**:
   ```bash
   dotnet ef migrations add AddAppVersioning
   ```

4. **Extend `AppFileStorageService`** with methods:
   - `CreateVersionSnapshot(appId, userId, versionNumber)` — Copy current/ to versions/vNNN/files/
   - `GetVersionFiles(appId, versionNumber)` — List files in version folder
   - `RestoreVersionFiles(appId, versionNumber)` — Copy version files back to current/

### Phase 2: Version Service

5. **Create `AppVersionService`** in `src/Automaton.Studio.Server/Services/`:
   ```csharp
   public interface IAppVersionService
   {
       Task<AppVersion> CreateSnapshotAsync(Guid appId, string userId, AppVersionTrigger trigger, string? sessionId);
       Task FinalizeVersionAsync(Guid appVersionId, string summary, List<FileChange> fileChanges);
       Task<List<AppVersionDto>> GetVersionsAsync(Guid appId, int page, int pageSize);
       Task<AppVersionDetailDto> GetVersionDetailAsync(Guid appId, int versionNumber);
       Task RestoreVersionAsync(Guid appId, int versionNumber, string userId);
       Task<string> GenerateVersionSummaryAsync(Guid appId, List<ChangeRecord> changes);
   }
   ```

6. **Step snapshot logic in `CreateSnapshotAsync`**:
   - Query all linked steps via `WebAppCustomSteps`
   - For each step, create `AppVersionStep` record with:
     - `CodeSnapshot` = `CustomStep.Code`
     - `VariablesSnapshot` = JSON serialize `InputVariables` + `OutputVariables`
   - No files written for steps — all in database

### Phase 3: API Endpoints

7. **Create `AppVersionsController`** in `src/Automaton.Studio.Server/Controllers/`:

   | Method | Route | Description |
   |--------|-------|-------------|
   | POST | `/apps/{id}/versions/snapshot` | Create snapshot (called on first change) |
   | POST | `/apps/{id}/versions/{v}/finalize` | Add summary after session |
   | GET | `/apps/{id}/versions` | List all versions |
   | GET | `/apps/{id}/versions/{v}` | Get version details |
   | POST | `/apps/{id}/versions/{v}/restore` | Restore to this version |
   | GET | `/apps/{id}/versions/{v}/files/{*path}` | Download specific file from version |

### Phase 4: Session-Based Versioning Integration

8. **Modify `AiChatHistoryService.CreateSessionAsync`**:
   - Before creating a new session for a WebApp, call `AppVersionService.CreateSnapshotAsync`
   - Pass `AppVersionTrigger.NewSession` as the trigger
   - Link the new session ID to the version after session creation

9. **Track changes during session**:
   - MCP tools record changes to a session-scoped change log (in memory or cache)
   - Change log format: `{ tool, filePath/stepId, action, timestamp }`
   - Used for generating version summary when next session starts

10. **Generate AI summaries**:
    - When creating a version, retrieve the change log from the previous session
    - Pass changes to AI for summary generation
    - Store summary in `AppVersion.Summary` and `FilesChangeSummary`

### Phase 5: AGENTS.md Implementation

11. **Create AGENTS.md on app creation**:
    - In `WebAppsService.CreateAsync()`, generate initial AGENTS.md
    - Template:
      ```markdown
      # {App Name}
      
      ## Description
      {App description from creation}
      
      ## Architecture
      - Frontend: HTML/CSS/JavaScript
      - Backend Steps: {list linked steps or "None yet"}
      
      ## Conventions
      {To be filled by AI or user}
      
      ## Known Issues
      {To be filled by AI or user}
      
      ## Recent Changes
      {Auto-updated with last 5 version summaries}
      ```

12. **Pass AGENTS.md to AI context**:
    - In `AppMcpToolProvider`, read AGENTS.md content on session start
    - Include in system prompt for all app-related tool calls
    - Add tool `update_agents_md` for AI to update conventions/issues

13. **Auto-update AGENTS.md**:
    - On version finalize, prepend summary to Recent Changes section
    - Keep only last 5 entries in Recent Changes (full history via API)

### Phase 6: Frontend Integration

14. **Extend `app-editor.store.ts`**:
    ```typescript
    // Add to store state
    currentSessionId: string | null;
    versions: AppVersionDto[];
    
    // Add actions
    loadVersions(): Promise<void>;
    createManualVersion(): Promise<void>;  // Manual save trigger
    restoreVersion(versionNumber: number): Promise<void>;
    ```

15. **Add Version History panel** in `AppEditorView.vue`:
    - Sidebar tab or expandable panel showing version list
    - Each entry: version number, date, summary (truncated), trigger badge
    - "Restore" button with confirmation dialog
    - Click to expand and see full summary + file/step changes
    - Link to associated AI chat session (if available)

16. **Session awareness**:
    - Display current session info in editor header
    - Show "New session started" notification when version auto-created
    - Manual Save button always available for explicit checkpoints

17. **Versioning Settings UI** in App Settings:
    - Toggle: Enable/disable auto-versioning on new session
    - Display version history stats (total versions, last version date)

18. **Version threshold monitoring**:
    - No active monitoring needed — versions created automatically on new session
    - Show version count badge in Version History tab

---

## Step Versioning Details

### Full Step Definition Snapshot

`AppVersionStep.VariablesSnapshot` JSON structure:

```json
{
  "inputVariables": [
    { "name": "email", "type": "string", "required": true, "description": "User email" },
    { "name": "password", "type": "string", "required": true, "description": "User password" }
  ],
  "outputVariables": [
    { "name": "isValid", "type": "boolean", "description": "Validation result" },
    { "name": "errors", "type": "array", "description": "List of validation errors" }
  ]
}
```

### Step Version Alignment

- **App version is the master** — Contains references to step snapshots at that point
- **No separate StepVersion table** — Steps are snapshotted as `AppVersionStep` rows
- **Restore behavior**: 
  - Restore app files from `versions/vNNN/files/`
  - Restore step code/variables from `AppVersionStep` records
  - Re-link steps that were linked at that version (via `Action = Linked`)
  - Unlink steps that weren't linked at that version

### Step Ownership Model

**Steps are single-app owned** — No step sharing between apps:
- Each step belongs to exactly one app
- **Step clone feature** (future): When users want similar functionality across apps, they clone the step
- Simplifies versioning — no cross-app conflicts during restore
- Simplifies restore — updating a step only affects one app

---

## Retention Policy

- **Default**: Keep last 50 versions per app
- **Configurable**: Via app settings (range: 10-100)
- **Protected versions**: Published versions (`Trigger = Publish`) are never auto-deleted
- **Cleanup**: Background job runs daily, deletes oldest versions beyond limit
- **Cascade**: Deleting `AppVersion` cascades to `AppVersionStep` rows and disk folder

---

## API DTOs

### `AppVersionDto` (list view)

```csharp
public class AppVersionDto
{
    public int VersionNumber { get; set; }
    public string Summary { get; set; }
    public AppVersionTrigger Trigger { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AiModel { get; set; }
    public int FileChangeCount { get; set; }
    public int StepChangeCount { get; set; }
}
```

### `AppVersionDetailDto` (single version)

```csharp
public class AppVersionDetailDto
{
    public int VersionNumber { get; set; }
    public string Summary { get; set; }
    public AppVersionTrigger Trigger { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AiModel { get; set; }
    public string? AiSessionId { get; set; }
    public List<FileChangeDto> FileChanges { get; set; }
    public List<StepChangeDto> StepChanges { get; set; }
}

public class FileChangeDto
{
    public string Path { get; set; }
    public string Action { get; set; }
    public string? Description { get; set; }
}

public class StepChangeDto
{
    public Guid StepId { get; set; }
    public string StepName { get; set; }
    public string Action { get; set; }
    public string? Description { get; set; }
}
```

---

## API Response Examples

### GET /apps/{id}/versions

```json
{
  "versions": [
    {
      "versionNumber": 3,
      "summary": "Added user authentication form with email validation. Created new backend step 'ValidateCredentials' for JWT token generation.",
      "trigger": "NewSession",
      "createdAt": "2026-02-05T10:30:00Z",
      "aiModel": "claude-sonnet-4-20250514",
      "fileChangeCount": 2,
      "stepChangeCount": 1
    },
    {
      "versionNumber": 2,
      "summary": "Initial layout with header and navigation menu.",
      "trigger": "NewSession",
      "createdAt": "2026-02-04T15:00:00Z",
      "aiModel": "gpt-4",
      "fileChangeCount": 1,
      "stepChangeCount": 0
    }
  ],
  "totalCount": 3,
  "page": 1,
  "pageSize": 20
}
```

### GET /apps/{id}/versions/3

```json
{
  "versionNumber": 3,
  "summary": "Added user authentication form with email validation. Created new backend step 'ValidateCredentials' for JWT token generation.",
  "trigger": "NewSession",
  "createdAt": "2026-02-05T10:30:00Z",
  "aiModel": "claude-sonnet-4-20250514",
  "aiSessionId": "sess_abc123",
  "fileChanges": [
    { "path": "index.html", "action": "Modified", "description": "Added login form HTML structure" },
    { "path": "styles.css", "action": "Modified", "description": "Added form styling and validation states" }
  ],
  "stepChanges": [
    { "stepId": "...", "stepName": "ValidateCredentials", "action": "Created", "description": "New step for JWT token validation and generation" }
  ]
}
```

---

## Resolved Decisions

| Decision | Resolution |
|----------|------------|
| Version trigger | New `AiChatSession` creation (simplest approach) |
| Session infrastructure | Use existing `AiChatSession` entity |
| Manual override | Save button always available for explicit checkpoints |
| Tracking complexity | No timers, no counters, no thresholds — just session boundaries |
| Backend vs Frontend | Version creation happens on backend in `AiChatHistoryService` |
| Summary generation timing | Generate when new session starts — uses accumulated change log from previous session |
| AGENTS.md editing | Both — UI textarea in App Settings + AI tool for automated updates |
| Cross-app step restore | Steps are not shared between apps; implement step clone to enforce single-app ownership |
| Version comparison | No comparison tool — summaries only (AI-first UX, users don't need diffs) |
| First version creation | On first AI session — captures the initial state before any AI changes |
