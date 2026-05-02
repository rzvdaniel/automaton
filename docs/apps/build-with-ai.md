# Building Automaton Apps with AI Coding Tools

Automaton apps are frontend-agnostic static web apps with Python backends. You can build them
inside your preferred AI coding environment — VS Code with GitHub Copilot, Claude Code,
or any tool that supports MCP — keep the project in Git, and deploy it to Automaton when ready.

---

## Prerequisites

1. **A running Automaton server** — local (`https://localhost:7091`) or hosted
2. **An API key with execute scope** — create one at **API Keys** → **New Key** → scope: `execute`
   - Format: `ak_execute_xxx`
3. **One-time MCP configuration** per tool if you want the AI to understand Automaton structure automatically

MCP is strongly recommended when your tool supports it because it gives the AI the most accurate
Automaton-specific guidance. It is not required. If you prefer a tool or workflow without MCP,
use the starter prompt below and deploy with the CLI.

On first use, create or link the app and keep `automaton.json` in your project root. On every
subsequent session, the AI can read that file and continue working on the same app.

`automaton.json` example (written by the AI, not by you):
```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App"
}
```

---

## VS Code — GitHub Copilot (Agent Mode)

**Requires:** VS Code 1.99+ and the GitHub Copilot extension.

### Setup

Create `.vscode/mcp.json` in your workspace (or add to user settings):

```json
{
  "servers": {
    "automaton": {
      "type": "http",
      "url": "https://localhost:7091/api/mcp",
      "headers": {
        "X-API-Key": "ak_execute_xxx"
      }
    }
  }
}
```

> VS Code accepts both `http://` and `https://` URLs directly — no extra settings needed.
> If your server uses a self-signed certificate, trust the dev certificate in your browser
> first (`https://localhost:7091` → accept the warning) so VS Code can connect.

### Using Copilot

1. Open Copilot Chat (`Ctrl+Alt+I`)
2. Switch to **Agent** mode using the dropdown next to the input box
3. Automaton tools appear automatically — Copilot discovers them on connection
4. Start with a prompt describing what to build:

```
Build this as an Automaton app. Use a Python backend in backend/ to store and retrieve todos.
Each backend controller must export handle(action_name, inputs), and frontend code should call
window.automatonApi.callController(...).
```

Copilot can:
- Create or link the app if `automaton.json` doesn't exist yet
- Generate the project in your local workspace
- Follow the Automaton backend/controller contract automatically through MCP guidance

When the project is ready, run `automaton deploy` to upload the frontend and deploy the backend.

### Iterating

Open `automaton.json` to see the appId, then in Copilot Chat:

```
Add a "clear completed" button that calls backend/todos.py → clear_completed()
```

Copilot reads existing files via `read_file` and `list_files`, updates only what changed.

---

## Claude Code

**Requires:** Claude Code CLI (`npm install -g @anthropic-ai/claude-code`)

### Setup (one-time per server)

```bash
claude mcp add automaton \
  --transport http \
  "https://localhost:7091/api/mcp" \
  --header "X-API-Key: ak_execute_xxx"
```

Verify the server is registered:

```bash
claude mcp list
```

### Using Claude Code

In your project folder:

```bash
cd /path/to/my-app
claude
```

Then describe what to build:

```
Build this as an Automaton app. Store scores in a Python backend in backend/.
Each controller must export handle(action_name, inputs), and frontend code should call
window.automatonApi.callController(...). Keep the project local and prepare it for automaton deploy.
```

Claude Code can:
- Check for `automaton.json` and link or create the app
- Generate the local project with the correct Automaton structure
- Reuse the same project folder across sessions

When the project is ready, run `automaton deploy`.

### Switching Projects

Each project folder has its own `automaton.json`. Claude Code reads from the current working
directory — `cd` to a different project and it automatically targets a different app.

```bash
cd /path/to/app-1   # works on app-1
cd /path/to/app-2   # works on app-2 — no reconfiguration needed
```

---

## GitHub Copilot CLI

**Requires:** GitHub CLI (`gh`) with the Copilot extension (`gh extension install github/gh-copilot`)

The Copilot CLI (`gh copilot suggest`, `gh copilot explain`) is a terminal assistant — it does not
connect to MCP servers directly. The recommended workflow is to use it to write code locally, then
push to Automaton via zip upload.

## Starter Prompt for Tools Without MCP

Paste this into Cursor, Claude Code, Copilot Chat, or another AI tool if MCP is not configured:

```text
Build this as an Automaton app. Use any static frontend stack that fits the task. Put Python backend controllers in backend/. Each controller file must export handle(action_name, inputs) and return { success, data, error }. Frontend code must call window.automatonApi.callController(controller, action, inputs) and use try/catch because the function returns data directly and throws on errors. Use backend/shared/ for shared helpers, backend/requirements.txt for extra Python packages, backend/mocks/{controller}.{action}.json for local mock responses, keep automaton.json in the project root, and deploy with automaton deploy.
```

### Workflow

**1. Generate the app locally**

```bash
gh copilot suggest "Create a simple weather dashboard HTML app with a Python backend that returns mock weather data"
```

Follow Copilot's suggestions to create the files in a local folder.

**2. Upload to Automaton**

First, create an app in the Automaton UI and copy its ID from the URL or settings page.

Then push the files:

```bash
cd /path/to/my-app
zip -r /tmp/app.zip . --exclude "*.git*"

curl -X POST "https://localhost:7091/api/apps/{appId}/upload" \
  -H "X-API-Key: ak_execute_xxx" \
  -F "file=@/tmp/app.zip"
```

**3. Deploy the backend**

After uploading, trigger backend deployment from the Automaton UI (Apps → your app → Deploy
Backend), or call the API:

```bash
curl -X POST "https://localhost:7091/api/apps/{appId}/execute/deploy" \
  -H "X-API-Key: ak_execute_xxx"
```

### Iterating with Copilot CLI

Use `gh copilot suggest` to generate changes, apply them to local files, then re-upload:

```bash
# Merge — only updated files overwrite existing app files
curl -X POST "https://localhost:7091/api/apps/{appId}/upload" \
  -H "X-API-Key: ak_execute_xxx" \
  -F "file=@/tmp/app.zip"

# Full replacement — clears all app files first
curl -X POST "https://localhost:7091/api/apps/{appId}/upload?clean=true" \
  -H "X-API-Key: ak_execute_xxx" \
  -F "file=@/tmp/app.zip"
```

---

## App Structure Reference

```
my-app/
├── automaton.json          ← app link + optional build/deploy config (commit this)
├── frontend/               ← plain static apps can live here
│   ├── index.html          ← entry point for plain static apps
│   └── *.css / *.js / assets/
├── src/                    ← framework source code can stay in the repo
├── dist/                   ← common build output for framework apps
└── backend/
  ├── *.py                ← Python controllers (filename = controller name)
    ├── requirements.txt    ← pip dependencies (optional)
  ├── shared/             ← shared Python helpers
    └── mocks/
        └── {ctrl}.{action}.json   ← local dev mock responses
```

Notes:
- Plain static apps can live directly in `frontend/`
- Framework apps such as React, Vue, or Angular can keep source code in the repo and deploy the built output, typically from `dist/`
- Use `build` and `deployDir` in `automaton.json` when the deployable frontend lives somewhere other than the default path

## Python Controller Pattern

```python
# backend/todos.py
class TodosController:
  def list(self, inputs: dict) -> dict:
    return {"success": True, "data": {"items": []}, "error": ""}

  def create(self, inputs: dict) -> dict:
    title = inputs.get("title", "").strip()
    if not title:
      return {"success": False, "data": None, "error": "title is required"}
    return {"success": True, "data": {"title": title}, "error": ""}

_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
  method = getattr(_controller, action_name, None)
  if method is None:
    return {"success": False, "data": None, "error": f"Unknown action: {action_name}"}
  return method(inputs)
```

Each backend controller file must export `handle(action_name, inputs)` and return the standard
response envelope: `{ success, data, error }`.

## Calling a Backend Controller from the Frontend

```javascript
try {
  const data = await window.automatonApi.callController('todos', 'list', { page: 1 })
  console.log(data.items)
} catch (err) {
  console.error(err.message)
}
```

`window.automatonApi` is provided by Automaton at runtime. `callController()` returns the backend
`data` payload directly and throws on error, so always use `try/catch`. In local development, the
dev polyfill in `index.html` reads from `backend/mocks/` automatically — no server needed.

---

## Local Development (no server required)

```bash
cd my-app
python -m http.server 8080
# open http://localhost:8080
```

The dev polyfill intercepts `callController` calls and reads from `backend/mocks/` instead.
No API key, no runner, no Automaton server needed for frontend development.

---

## Comparison

| | VS Code + Copilot | Claude Code | Copilot CLI |
|---|---|---|---|
| MCP integration | ✅ Native (Agent mode) | ✅ Native | ❌ (zip upload instead) |
| Auto-creates app | ✅ | ✅ | ❌ (manual in UI) |
| `automaton.json` | ✅ Written by Copilot | ✅ Written by Claude | ❌ Not applicable |
| Multi-app support | ✅ Per-folder | ✅ Per-folder | ✅ appId in curl |
| Local Git history | Optional | Optional | ✅ Natural fit |
| Best for | Daily dev in VS Code | Terminal-first workflow | Generating & batch uploading |
