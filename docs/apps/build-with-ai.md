# Building Automaton Apps with AI

Automaton apps are frontend-agnostic static web apps with Python backends. There are three ways
to build them, depending on how much local tooling you want to use.

---

## Three Ways to Build

| Path | Where you work | Best for |
|---|---|---|
| **1. Studio App Editor** | Browser — Automaton Studio | Fastest start, quick prototypes |
| **2. Local IDE + CLI** | Your editor (Cursor, VS Code, etc.) | Full Git workflow, larger apps |
| **3. MCP** | VS Code Agent / Claude Code | Most accurate AI guidance when supported |

Paths 2 and 3 overlap — MCP is an optional enhancement to the local IDE workflow. You can use
Path 2 without MCP by pointing your AI tool to the `llms.txt` file instead.

---

## Path 1: Studio App Editor

The fastest way to go from idea to running app. No local setup, no CLI, no Git required.

1. Open **Automaton Studio** → **Apps** → **New App**.
2. Open the app in the **App Editor**.
3. Add your own AI API key under **Settings** → **AI Keys** (OpenAI, Anthropic, Gemini, etc.).
4. Describe what you want in the **AI Chat** panel. The editor generates frontend and backend files.
5. Click **Deploy Backend** when the backend changes.
6. Click **Publish** when ready.

The App Editor is the fastest path to a working prototype. When the app grows or you want full
Git and tooling control, switch to the CLI workflow.

Studio documentation is available at `/docs` inside Automaton Studio.

---

## Path 2: Local IDE + CLI

Build the app in your preferred editor, keep it in Git, and deploy to Automaton when ready.

### Prerequisites

1. **A running Automaton server** — local (`https://localhost:7091`) or hosted (`https://api.automaton.monster`)
2. **An API key** — create one at **API Keys** → **New Key** → scope: `execute`
   - Format: `ak_execute_xxx`
3. **Automaton CLI** — install once: `dotnet tool install -g Automaton.Cli` (requires .NET 9)

### Teach your AI the Automaton structure

AI tools have no prior knowledge of the Automaton app contract. Point your tool to the
`llms.txt` file so it follows the correct structure automatically:

```
https://automaton.monster/llms.txt
```

The file links to detailed instructions covering frontend structure, backend controller pattern,
`callController()` usage, mock responses, and ready-to-paste starter prompts.

Start your session with a prompt like:

```text
Read https://automaton.monster/llms.txt, then build an Automaton app for Poker Planning.
```

If you use the CLI happy path, `automaton setup`, `automaton new`, and `automaton link` also write a local guidance pack into `.automaton/ai-guidance/` plus generated `AGENTS.md` and `.github/` instructions. If those generated files need a manual refresh later, run `automaton sync-ai`.

Or paste the generic starter prompt directly if your tool does not fetch URLs:

```text
Build this as an Automaton app. Use any static frontend stack that fits the task. Put Python
backend controllers in backend/. Each controller file must export handle(action_name, inputs)
and return { success, data, error }. Frontend code must call
window.automatonApi.callController(controller, action, inputs) and use try/catch because the
function returns data directly and throws on errors. Use backend/shared/ for shared helpers,
backend/requirements.txt for extra Python packages,
backend/mocks/{controller}.{action}.json for local mock responses, keep automaton.json in the
project root. If the app has Python backend controllers, run automaton runners, assign a runner
with automaton set-runner <runner-id>, or create/deploy one in Automaton Studio if none is connected, and then deploy with automaton deploy.
```

Starter prompts for specific stacks (plain HTML, React, Vue) are available at
`https://automaton.monster/llms/automaton-starter-prompts.md`.

### AI-readable reference files

| File | Purpose |
|---|---|
| [`/llms.txt`](https://automaton.monster/llms.txt) | Entry point — start here |
| [`/llms/automaton-apps-overview.md`](https://automaton.monster/llms/automaton-apps-overview.md) | Rules, workflow, recommended structure |
| [`/llms/automaton-frontend-structure.md`](https://automaton.monster/llms/automaton-frontend-structure.md) | Plain HTML, React, Vue layouts |
| [`/llms/automaton-backend-controllers.md`](https://automaton.monster/llms/automaton-backend-controllers.md) | Python controller contract |
| [`/llms/automaton-starter-prompts.md`](https://automaton.monster/llms/automaton-starter-prompts.md) | Copy-paste prompts per stack |

### `automaton.json`

Created by the AI (or manually) at the project root. Commit it to Git.

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "server": "https://api.automaton.monster"
}
```

Add `build` and `deployDir` for framework apps:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

If `automaton.json` already exists, the AI should add missing `build` and `deployDir` fields instead of leaving deploy behavior implicit.

### Deploy

Recommended local flow for a new app:

```bash
automaton setup
automaton new "My App"
```

For an existing remote app:

```bash
automaton setup
automaton link <app-id>
```

For backend apps, pick an existing connected runner once before the first backend deploy, or create/deploy one in Automaton Studio if none exists:

```bash
automaton runners
automaton set-runner <runner-id>
```

```bash
automaton deploy
```

This uploads frontend files and triggers backend deployment. Re-run after any change.

Server URL resolution order for CLI deploy is:

1. `AUTOMATON_SERVER`
2. `server` in `automaton.json`
3. `./.automaton/config.json`
4. `~/.automaton/config.json`
5. interactive prompt

If the app has no backend scripts, a successful deploy may finish with `No backend scripts found`.

---

## Path 3: MCP

MCP gives the AI the most accurate Automaton-specific guidance by connecting it directly to
your Automaton server. Use it in tools that support MCP alongside the local IDE workflow.

If your tool does not support MCP — or you are in a session without MCP configured — use
`https://automaton.monster/llms.txt` instead (Path 2 above).

### VS Code — GitHub Copilot (Agent Mode)

**Requires:** VS Code 1.99+ and the GitHub Copilot extension.

#### Setup

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

#### Using Copilot

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
- Add missing `build` and `deployDir` fields to `automaton.json` when the app builds to `dist/` or another output folder
- Generate the project in your local workspace
- Follow the Automaton backend/controller contract automatically through MCP guidance

When the project is ready, pick an existing connected runner with `automaton set-runner <runner-id>` for backend apps, or create/deploy one in Automaton Studio if none exists, then run `automaton deploy` to upload the frontend and deploy the backend.

#### Iterating

Open `automaton.json` to see the appId, then in Copilot Chat:

```
Add a "clear completed" button that calls backend/todos.py → clear_completed()
```

Copilot reads existing files via `read_file` and `list_files`, updates only what changed.

---

### Claude Code

**Requires:** Claude Code CLI (`npm install -g @anthropic-ai/claude-code`)

#### Setup (one-time per server)

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

#### Using Claude Code

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
- Add missing `build` and `deployDir` fields to `automaton.json` when the deployable frontend is built output
- Generate the local project with the correct Automaton structure
- Reuse the same project folder across sessions

When the project is ready, pick an existing connected runner with `automaton set-runner <runner-id>` for backend apps, or create/deploy one in Automaton Studio if none exists, then run `automaton deploy`.

#### Switching Projects

Each project folder has its own `automaton.json`. Claude Code reads from the current working
directory — `cd` to a different project and it automatically targets a different app.

```bash
cd /path/to/app-1   # works on app-1
cd /path/to/app-2   # works on app-2 — no reconfiguration needed
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

| | Studio App Editor | Local IDE + CLI (llms.txt) | VS Code Copilot (MCP) | Claude Code (MCP) |
|---|---|---|---|---|
| Local setup required | ❌ | ✅ | ✅ | ✅ |
| Git workflow | ❌ | ✅ | ✅ | ✅ |
| MCP integration | N/A | ❌ (llms.txt instead) | ✅ Native | ✅ Native |
| AI knows Automaton structure | ✅ Built-in | ✅ Via llms.txt | ✅ Via MCP | ✅ Via MCP |
| Auto-creates `automaton.json` | ✅ | ✅ | ✅ | ✅ |
| Multi-app support | ✅ Per app | ✅ Per folder | ✅ Per folder | ✅ Per folder |
| Best for | Quick prototypes | Any IDE, any AI tool | Daily dev in VS Code | Terminal-first workflow |
