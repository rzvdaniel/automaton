# Building Automaton Apps with AI Coding Tools

Automaton apps are HTML/CSS/JavaScript apps with optional Python backends. You can build them
entirely inside your preferred AI coding environment — VS Code with GitHub Copilot, Claude Code,
or any tool that supports MCP — and push them to Automaton without opening the browser editor.

---

## Prerequisites

1. **A running Automaton server** — local (`https://localhost:7091`) or hosted
2. **An API key with execute scope** — create one at **API Keys** → **New Key** → scope: `execute`
   - Format: `ak_execute_xxx`
3. **One-time MCP configuration** per tool (see below)

On first use, the AI will call `create_app` automatically and write an `automaton.json` file to
your project root. On every subsequent session, it reads that file and continues working on the
same app — no manual setup per project.

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
Build a simple todo list app. Use a Python backend to store and retrieve todos.
Use the automaton tools to write the files directly.
```

Copilot will:
- Call `create_app` if `automaton.json` doesn't exist yet
- Write `index.html`, CSS, JS, and `backend/*.py` files via `write_file`
- Call `deploy_backend` to push the Python files to the runner

### Iterating

Open `automaton.json` to see the appId, then in Copilot Chat:

```
Add a "clear completed" button that calls backend/todo.py → clear_completed()
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
Build a leaderboard app. Store scores in a Python backend. Show the top 10 scores on the page.
Use the automaton MCP tools to write and deploy the files.
```

Claude Code will:
- Check for `automaton.json` → create the app if it doesn't exist
- Write all files to Automaton via the MCP tools
- Call `deploy_backend` when backend files are ready

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
├── automaton.json          ← written by the AI on first run (commit this)
├── index.html              ← required entry point
├── *.css / *.js / assets/  ← frontend — any structure you prefer
└── backend/
    ├── *.py                ← Python controllers (filename = controller name)
    ├── requirements.txt    ← pip dependencies (optional)
    └── mocks/
        └── {ctrl}.{action}.json   ← local dev mock responses
```

## Python Controller Pattern

```python
# backend/todo.py
def list_todos(inputs):
    return {'todos': []}

def add_todo(inputs):
    text = inputs.get('text', '')
    return {'added': True, 'text': text}
```

## Calling a Backend Controller from the Frontend

```javascript
// Calls backend/todo.py → list_todos()
const result = await window.automatonApi.callController('todo', 'list_todos', {});

// Calls backend/todo.py → add_todo()
await window.automatonApi.callController('todo', 'add_todo', { text: 'Buy milk' });
```

`window.automatonApi` is provided by Automaton at runtime. In local development, the dev
polyfill in `index.html` reads from `backend/mocks/` automatically — no server needed.

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
