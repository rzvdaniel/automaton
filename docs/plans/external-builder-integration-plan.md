# External Builder Integration

Enable developers to build Automaton apps in their own AI-powered environments (Claude Code, Cursor, Codex, GitHub Copilot, etc.) and deploy directly to Automaton. Automaton becomes an app repository and execution platform — not just an AI editor.

---

## Positioning

### Why "Build Anywhere, Deploy to Automaton" Is a Strong Growth Strategy

The traditional model — build inside Automaton's AI editor — competes directly with Claude Code,
Cursor, and Windsurf. These are tools developers already love and are deeply invested in. Asking
them to switch is a hard sell.

The new model flips the dynamic: **those tools become feeders rather than competitors.**
Every Claude Code user is a potential Automaton user. The platform grows by being useful to
existing workflows instead of replacing them.

### The Real Value Proposition Shifts

| Before | After |
|---|---|
| "Use our AI editor" (competing with Claude Code) | "Deploy to Automaton" (complementing Claude Code) |
| Walled garden | Open platform |
| One AI model limit | Any AI the user prefers |
| Hard to attract experienced devs | Experienced devs onboard naturally |

### Three Distinct User Segments

**1. AI-native developers** — already using Claude Code/Cursor daily. They want backend infra
(runners, Python controllers, real-time execution) without building it. Automaton becomes their
deployment target, not their editor.

**2. Organizations** — they want internal tools built fast and shared securely. The
project/membership model Automaton already has is exactly what enterprises need. Teams build with
their own licensed AI tools and host on Automaton.

**3. App marketplace users** — public apps at `/web/{slug}`, forkable templates, sample gallery.
Automaton becomes a community where apps can be discovered and remixed — like a GitHub for
runnable mini-apps.

### The "Repository" Framing

GitHub doesn't build your code. Vercel doesn't write your frontend. But developers use them every
day because they solve deployment and sharing, not creation.

Automaton's unique angle in this space:

- Apps have **live Python backends** — not just static hosting
- Apps can **execute flows** and interact with automation pipelines
- Apps can be **scoped to organizations** with real access control
- The backend runs **in your infrastructure** via private runners

None of Claude Code, Cursor, or Vercel offer that combination.

### The Risk to Manage

The internal AI editor remains valuable for non-developers — people who can't use Claude Code
because it's too technical. Don't abandon it. The power move is supporting both:

| User | Path |
|---|---|
| Non-developer | Build inside Automaton with the AI editor |
| Developer | Build with their own tools, push to Automaton |

Both end up on the same platform, sharing apps, flows, and runners.

### Git Ownership

With the "build anywhere" model, developers own their source in a standard Git repository.
Automaton becomes the deployment target — not the source of truth for code.

This mirrors how Vercel and Netlify work for web hosting: your code lives in GitHub,
the platform handles serving and infrastructure. For Automaton the same model applies,
but instead of a static site you get a live Python backend on a private runner.

Practical implications:

- **Version control is free** — developers use their normal Git workflow (branches, PRs, code review)
- **Team collaboration happens in GitHub/GitLab**, not inside Automaton's editor
- **Rollback = `git checkout` + re-upload** — no dependency on Automaton's versioning
- **CI/CD is straightforward** — a single `curl` to `/api/apps/{id}/upload` can be triggered from
  any GitHub Actions workflow, pipeline, or deployment script
- **Apps can be open-source** — publish the repo, let others fork it and deploy to their own
  Automaton instance

This is a meaningful advantage over the walled-garden model, where code lives only on the
platform and is locked in. Experienced developers will strongly prefer owning their own source.

### Automaton vs. Vercel and Netlify

Vercel and Netlify are the closest analogies — but the comparison has important limits.

**What they share:**

| Trait | Vercel / Netlify | Automaton |
|---|---|---|
| Deployment model | push code, platform serves it | push code, platform serves it |
| Source ownership | your Git repo | your Git repo |
| CI/CD integration | Git hooks / webhooks | `curl` to `/api/apps/{id}/upload` |
| Public URL | `your-app.vercel.app` | `/web/{slug}` |

**Where Automaton is fundamentally different:**

| Aspect | Vercel / Netlify | Automaton |
|---|---|---|
| Backend execution | serverless functions (cloud-only) | Python scripts on **your own runners** |
| Infrastructure | vendor's cloud | **self-hosted** (on-prem, private VPC, etc.) |
| App type | web sites and APIs | **automation-first**: flows, triggers, pipelines |
| Audience | general web developers | teams running internal tools and automations |
| Multi-tenancy | projects per account | projects with **role-based membership** |
| AI integration | none built in | flows generated by AI, apps call flows |

**The key distinction:** Vercel and Netlify host *output* — compiled frontend artifacts served over
CDN. Automaton hosts *running processes* — Python controllers that execute on private compute,
have access to internal systems, and can trigger multi-step automation flows. The deployment
model looks similar on the surface but the runtime is completely different.

**When Vercel/Netlify is the right choice:**
A marketing site, a SaaS frontend, a documentation portal — anything that is pure static or
calls third-party APIs directly. No runners, no flow engine needed.

**When Automaton is the right choice:**
An internal tool that reads from a private database, runs a data pipeline, controls infra,
or integrates with flows that span multiple steps and external systems — and needs to be scoped
to specific users in an organization.

---

## Vision

- Developer builds HTML/CSS/JS + Python app locally using any AI tool (Claude Code, Cursor, Copilot, etc.)
- Pushes to Automaton via zip upload or MCP file tools — no browser editor required
- Automaton handles file storage, backend deployment to runners, and publishing
- App is live at `/web/{slug}` with Python controllers executing on the runner

---

## What Was Built

### 1. Dev Polyfill — `wwwroot/scripts/automaton-dev.js`

A JavaScript IIFE that provides `window.automatonApi` for local testing. When a developer opens
an app with a plain static server (`python -m http.server`), `callController` reads from
`backend/mocks/{ctrl}.{action}.json` instead of hitting a real backend. In production, Automaton
injects its own `automatonApi` first — the guard `if (window.automatonApi) return` makes the
polyfill a no-op.

### 2. Default Template Updated — `Services/AppsService.cs` → `GetDefaultHtmlContent()`

Every new app gets the polyfill script tag in `<head>` automatically. No manual setup needed.

```html
<!-- Dev polyfill: provides window.automatonApi for local testing via backend/mocks/*.json files.
     Automaton injects its own implementation at serve time — this block becomes a no-op. -->
<script src="/scripts/automaton-dev.js"></script>
```

### 3. Backend Path Enforcement — `Services/Mcp/Apps/AppScopedFileSystem.cs`

`WriteFileAsync` rejects `.py` and `.bat` files placed anywhere except `backend/`. The error
message tells the AI exactly what path to use. Fires at the tool level — cannot be bypassed
by instructions alone.

### 4. Zip Upload — `Controllers/AppsController.cs` → `POST /api/apps/{id}/upload`

Accepts a `.zip` multipart upload authenticated via API key or JWT. Two modes via query param:

| Mode | Behaviour |
|---|---|
| `?clean=false` (default) | Merge — adds/overwrites only files in the zip |
| `?clean=true` | Full replace — deletes existing app files first, then extracts |

Security: zip-slip path traversal guard, extension whitelist, `.py`/`.bat` placement enforcement,
`_metadata.json` is never overwritten.

Response: `{ "written": [...], "skipped": [...], "errors": [...] }`

Frontend method added: `appsService.uploadZip(id, file, clean)`.

### 5. External MCP Server — `Controllers/ExternalAppMcpController.cs`

A JSON-RPC 2.0 endpoint at `POST /api/mcp/apps/{appId}`. Implements the MCP protocol so Claude
Code, Cursor, and other AI tools can connect directly to an Automaton app.

**MCP tools exposed:**

| Tool | Description |
|---|---|
| `write_file` | Write any app file (enforces `backend/` rule) |
| `read_file` | Read a file back |
| `list_files` | List all app files |
| `delete_file` | Delete one or more files |
| `deploy_backend` | Push `backend/*.py` scripts to the runner |

On `initialize`, the controller reads the MCP prompt files under `SystemPrompts/`, replaces
`{server}`, `{appId}`, and `{apiKey}` with the actual request values, and sends the rendered
instructions to the AI tool. The AI key is read from the `X-API-Key` header or `Bearer ak_xxx`
token — whichever the developer configured when adding the MCP server.

### 6. Instructions File — `SystemPrompts/ExternalMcpInstructions.md`

The prompt the AI receives at connection time comes from a single MCP instructions file. Shared app-building rules should live in `llms.txt` and the linked `llms/` docs, while the MCP file should stay focused on MCP behavior and tool usage.

Editable without recompiling — marked `CopyToOutputDirectory: PreserveNewest` in the `.csproj`.

---

## Developer Workflows

### Incremental — AI writes file by file via MCP

One-time setup:

```bash
claude mcp add automaton \
  --transport http \
  "https://your-server/api/mcp/apps/{appId}" \
  --header "X-API-Key: ak_execute_xxx"
```

From that point Claude Code uses `write_file`, `read_file`, `list_files`, `delete_file`,
and `deploy_backend` tools directly against the live app.

### Bulk — AI builds locally, pushes as zip

```bash
cd /path/to/app
zip -r /tmp/app.zip . --exclude "*.git*"

# Merge upload (delta — only changed files)
curl -X POST "https://your-server/api/apps/{appId}/upload" \
  -H "X-API-Key: ak_execute_xxx" \
  -F "file=@/tmp/app.zip"

# Full replacement
curl -X POST "https://your-server/api/apps/{appId}/upload?clean=true" \
  -H "X-API-Key: ak_execute_xxx" \
  -F "file=@/tmp/app.zip"
```

Both workflows enforce the same backend rules. The curl commands are included in the MCP
instructions with real values, so Claude Code can use either approach in the same session.

---

## Local Testing (no server required)

```bash
cd /path/to/app && python -m http.server 8080
# open http://localhost:8080
```

`window.automatonApi` reads from `backend/mocks/` automatically via the dev polyfill in
`index.html`. No runner, no Automaton server, no API key needed.

---

## App Structure Convention

```
my-app/
├── index.html              ← required entry point
├── *.css / *.js / assets/  ← frontend — any folder structure
└── backend/
    ├── *.py                ← Python controllers (filename = controller name)
    ├── requirements.txt    ← pip dependencies (optional)
    └── mocks/
        └── {ctrl}.{action}.json  ← local dev mock responses
```

## Python Controller Pattern

```python
# backend/score.py
def get_top(inputs):
    limit = inputs.get('limit', 10)
    return {'scores': []}

def save(inputs):
    return {'saved': True}
```

## Calling a Controller from the Frontend

```javascript
const result = await window.automatonApi.callController('score', 'get_top', { limit: 5 });
```

---

## Out of Scope (Future)

- CLI tool (`automaton-push`) — wraps zip upload, reads `automaton.json` for server/appId/key
- GitHub Actions integration — push to Automaton on `git push`
- Compiled framework support (React/Vue `dist/` folder upload)
- App marketplace / public discovery page
- Versioning and rollback for externally uploaded apps
- Automaton handles file storage, backend deployment to runners, publishing
- App is live at `/web/{slug}` with Python controllers executing on the runner

---

## App Structure Convention

All external apps must follow this layout:

```
my-app/
├── index.html              ← required entry point
├── style.css               ← optional
├── *.js                    ← optional JS modules
├── assets/                 ← images, fonts, etc.
└── backend/
    ├── *.py                ← Python controllers (one file = one controller)
    ├── *.bat               ← Batch controllers (Windows runners only)
    ├── requirements.txt    ← pip dependencies (optional)
    └── mocks/
        └── {ctrl}.{action}.json  ← local dev mock responses
```

**Rules enforced at upload time:**
- `index.html` must exist at root
- `.py`/`.bat` files only allowed inside `backend/`
- Only allowed extensions (existing whitelist in `AppFileStorageService`)

---

## Stack

- **Frontend:** HTML / CSS / vanilla JS (CDN libraries allowed via `<script>` tags)
- **Backend:** Python controllers in `backend/` (executed on runner nodes)
- **Testing:** Mock responses in `backend/mocks/*.json` (no runner needed)

---

## Local Development Pattern

Every new app's `index.html` includes a dev polyfill reference:

```html
<script src="/scripts/automaton-dev.js"></script>
```

`automaton-dev.js` (served from server `wwwroot/scripts/`):
- Provides `window.automatonApi` shim that reads from `backend/mocks/`
- Is a no-op when Automaton's real `automatonApi` is already injected
- Safe to keep in `index.html` in production — guard pattern prevents double injection

---

## Components to Build

### 1. `automaton-dev.js` — Dev Polyfill

**File:** `src/Automaton.Studio.Server/wwwroot/scripts/automaton-dev.js`

```javascript
if (!window.automatonApi) {
  window.automatonApi = {
    callController: async (ctrl, action, inputs) => {
      const r = await fetch(`backend/mocks/${ctrl}.${action}.json`)
      const m = await r.json()
      return m.data ?? m
    },
    executeFlow: async () => ({ success: true, data: {} }),
    uploadFile: async () => ({ session_id: 'dev-session' }),
    downloadFile: async () => {},
    getBackendMode: () => 'mock',
    setBackendMode: (m) => m
  }
  console.log('[AutomatonAPI] Dev polyfill loaded (mock mode)')
}
```

**Change:** Update `GetDefaultHtmlContent()` in `AppsService.cs` to include `<script src="/scripts/automaton-dev.js"></script>` in `<head>`.

---

### 2. Zip Import Endpoint

**Endpoint:** `POST /api/apps/{id}/import`  
**Auth:** JWT (owner) or API key (`ak_execute_xxx`)  
**Content-Type:** `multipart/form-data`

**Logic:**
1. Accept `.zip` file upload
2. Validate: `index.html` exists at root
3. For each file in zip:
   - Validate extension against `AppFileStorageService.AllowedExtensions`
   - Reject `.py`/`.bat` outside `backend/`
   - Write via `AppFileStorageService.WriteAppFileAsync`
4. Optionally trigger backend deploy to configured runner
5. Return summary: files written, backend scripts deployed

**Request:**
```
POST /api/apps/{id}/import
Content-Type: multipart/form-data

file: <zip file>
deployBackend: true   (optional, default false)
```

**Response:**
```json
{
  "success": true,
  "filesWritten": 12,
  "backendScripts": 2,
  "deployed": true,
  "message": "Imported 12 files, deployed 2 backend scripts"
}
```

**File:** `src/Automaton.Studio.Server/Controllers/AppsController.cs` — new `Import` action.

---

### 3. MCP `upload_app` Tool

For Claude Code users connecting via MCP — uploads files directly without a zip, one call per session.

**Tool name:** `upload_app`  
**In:** `Automaton.Mcp/Tools/AppUploadTool.cs` (new file)

```json
{
  "name": "upload_app",
  "description": "Upload app files to Automaton. Provide all files as a dictionary of relative path → content. Follows app structure rules: index.html at root, .py/.bat only in backend/.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "appId": { "type": "string" },
      "files": {
        "type": "object",
        "description": "Map of relative path to file content, e.g. { \"index.html\": \"...\", \"backend/score.py\": \"...\" }"
      },
      "deployBackend": { "type": "boolean", "description": "Deploy backend scripts to runner after upload (default false)" }
    },
    "required": ["appId", "files"]
  }
}
```

**Implementation:** Iterates `files`, calls `AppFileStorageService.WriteAppFileAsync` for each, optionally calls `AppsControllerExecutionService.DeployBackendToRunnersAsync`.

**Registration:** Add `AppUploadTool` to `IMcpTool` registrations in `Program.cs`.

---

### 4. `automaton.json` Project Config

Convention for binding a local project to an Automaton app. Claude Code and external tools read this to know where to deploy.

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "serverUrl": "https://your-automaton-server.com",
  "apiKey": "ak_execute_xxx"
}
```

**Location:** Project root (alongside `index.html`).  
**`.gitignore` entry:** `automaton.json` (contains API key — should not be committed).

No code change needed — this is a convention. The zip import and MCP tool use it as context.

---

### 5. Structure Enforcement in `AppScopedFileSystem`

Hard enforcement at the tool level so misplaced files are rejected regardless of what Claude Code was told.

**File:** `src/Automaton.Studio.Server/Services/Mcp/Apps/AppScopedFileSystem.cs`

```csharp
public override async Task WriteFileAsync(string relativePath, string content)
{
    var ext = Path.GetExtension(relativePath).ToLowerInvariant();
    var normalizedPath = relativePath.Replace('\\', '/');

    if ((ext == ".py" || ext == ".bat") && !normalizedPath.StartsWith("backend/"))
        throw new InvalidOperationException(
            $"Python and Batch files must be placed in backend/. Use path 'backend/{Path.GetFileName(relativePath)}'");

    await base.WriteFileAsync(relativePath, content);
}
```

---

### 6. System Prompt Update (`app-base.md`)

Add structure rules to the base system prompt so both internal AI and external MCP sessions follow the same convention.

**File:** `src/Automaton.Mcp/SystemPrompts/AppPrompts/app-base.md`

Add section:

```markdown
## APP STRUCTURE — REQUIRED LAYOUT
All apps must follow this exact layout or deployment will fail.

- `index.html` — REQUIRED at root, the only entry point
- `*.css`, `*.js`, `assets/` — static files at root or subdirectories  
- `backend/*.py` — Python controllers (filename = controller name)
- `backend/requirements.txt` — pip dependencies (optional)
- `backend/mocks/{ctrl}.{action}.json` — mock responses for local testing

NEVER place `.py` or `.bat` files outside `backend/`.
NEVER call `window.automatonApi` injection manually — Automaton injects it at serve time.
The `<script src="/scripts/automaton-dev.js"></script>` tag in index.html is a dev polyfill — keep it.
```

---

## Implementation Order

1. `automaton-dev.js` + update `GetDefaultHtmlContent()` — standalone, no dependencies
2. Structure enforcement in `AppScopedFileSystem.WriteFileAsync` — standalone
3. System prompt update in `app-base.md` — standalone
4. Zip import endpoint — depends on 2 (validation reuse)
5. MCP `upload_app` tool — depends on 2 (same validation path)

Items 1–3 are independent and can be done in parallel.

---

## Out of Scope (Future)

- CLI tool (`automaton-push`) — wraps zip import, reads `automaton.json`
- GitHub Actions integration — push to Automaton on `git push`
- Compiled framework support (React/Vue `dist/` folder upload)
- App marketplace / public discovery page
- Versioning and rollback for externally uploaded apps

---

## Open Questions

1. Should zip import auto-publish the app, or leave it as draft?
2. Should `upload_app` MCP tool clear existing files before writing (full replace) or merge (incremental update)?
3. Should `automaton.json` be `.gitignore`'d by default, or should we provide a separate `automaton.local.json` for secrets?
