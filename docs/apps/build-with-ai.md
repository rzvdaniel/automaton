# Building Automaton Apps with AI

Automaton apps are frontend-agnostic static web apps with Python backends. There are two main ways
to build them, depending on how much local tooling you want to use.

---

## Two Ways to Build

| Path | Where you work | Best for |
|---|---|---|
| **1. Studio App Editor** | Browser — Automaton Studio | Fastest start, quick prototypes |
| **2. Local IDE + CLI** | Your editor (Cursor, VS Code, etc.) | Full Git workflow, larger apps |

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

## Current contract

Keep the model simple:

- Local authoring usually uses `frontend/` and `backend/`.
- Server-side editable files live in `source/frontend/` and `source/backend/`.
- Deploy writes runtime files to `live/frontend/` and `live/backend/`.
- Public serving reads only `live/frontend/`.
- Runner execution reads only `live/backend/`.

### Default flow: AI-first

Keep this as simple as possible for the user:

1. Install the CLI once.
2. Run `automaton login`.
3. Open your preferred AI tool in the app folder.
4. Ask it to build and deploy the app to Automaton Studio.

After `automaton login`, the AI should usually handle the rest: local file generation, app creation or linking, deploy preflight, runner selection for backend apps, deploy, and ask before publish. The user should usually only need to step in for interactive auth, runner creation outside the CLI, or publish approval.

If `automaton new` cannot create the remote app, the AI should keep the generated local scaffold, treat the project as unlinked, continue building locally, and then use `automaton create` or `automaton link <app-id>` before deploy.

### Fallback flow: manual CLI

Use this only when you want to drive the CLI yourself:

1. Install the CLI once.
2. Run `automaton login`.
3. Run `automaton new "My App"` or `automaton link <app-id>`.
4. Build locally.
5. Run `automaton deploy`.
6. Run `automaton publish` only when you want the app publicly accessible.
7. Run `automaton validate` only when you need a separate diagnostic check.

### Prerequisites

1. **A running Automaton server** — local (`https://localhost:7091`) or hosted (`https://api.automaton.monster`)
2. **An API key** — create one at **API Keys** → **New Key** → scope: `execute`
   - Format: `ak_execute_xxx`
3. **Automaton CLI** — install once: `dotnet tool install -g Automaton.Cli` (requires .NET 9)

### Teach your AI the Automaton structure

If you already ran `automaton new` or `automaton link` in the project, prefer the local generated guidance files first. Use `llms.txt` as the canonical source of truth and as the fallback when the tool does not honor local guidance files yet:

```
https://automaton.monster/llms.txt
```

The file links to detailed instructions covering frontend structure, backend controller pattern,
`callController()` usage, mock responses, and ready-to-paste starter prompts.

Fallback prompt when the tool does not consume local guidance files:

```text
Build a Poker Planning app for Automaton Monster using the Automaton CLI. Start by running automaton login, then create or link the app in this folder. Follow the generated .automaton guidance after automaton new or automaton link. If local guidance is unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL.
```

If you use the CLI happy path, `automaton new` and `automaton link` write canonical local guidance into `.automaton/agents/` plus a small root `AGENTS.md` adapter. Detailed references live under `.automaton/agents/references/`. After that, the AI should usually follow those local files directly. If those generated files need a manual refresh later, run `automaton sync-ai`.

Or paste the generic starter prompt directly if your tool does not fetch URLs:

```text
Build [describe the app] for Automaton Monster using the Automaton CLI. Start by running automaton
login, then create or link the app in this folder and follow the generated .automaton guidance. If local guidance is
unavailable, read https://automaton.monster/llms.txt. Deploy with automaton deploy. If deploy
succeeds, ask whether to publish; if I approve, run automaton publish and return the public URL.
Only ask me to step in for authentication or publish approval.
```

Starter prompts for specific stacks (plain HTML, React, Vue) are available at
`https://automaton.monster/llms/automaton-starter-prompts.md`.

### AI-readable reference files

Use these as the canonical source of truth and the fallback reference set when local generated guidance is unavailable:

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

For the default AI-generated local app, use nested frontend/backend config:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

If `automaton.json` already exists, the AI should add missing `frontend` or `backend` deploy fields instead of leaving deploy behavior implicit.

If the app is only scaffolded locally and not linked yet, this temporary file shape is valid:

```json
{
  "name": "My App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

Add `appId` after `automaton create` or `automaton link <app-id>`.

### Deploy

Recommended local flow for a new app:

```bash
automaton login
automaton new "My App"
```

For an existing remote app:

```bash
automaton login
automaton link <app-id>
```

After `automaton login`, the AI should be able to handle most of the remaining work in the local folder: scaffold or edit files, create or link the app, keep `automaton.json` correct, refresh generated guidance only if needed, deploy, and ask before publish. The user should usually only need to step in for interactive auth, runner creation outside the CLI, or publish approval.

For backend apps, `automaton deploy` selects or prompts for a connected runner when one is needed. If no connected runner exists, deploy may still succeed with warnings after uploading `live/frontend` and `live/backend`; in that case backend is uploaded but not running yet. To make it runnable later:

```bash
automaton deploy
automaton deploy --runner <runner-id-or-name>
```

This uploads `frontend.deployDir` as public frontend runtime, uploads `backend.deployDir` as runner backend runtime when present, and then triggers backend deployment. Re-run after any change.

If the app has no backend scripts, a successful deploy may finish with `No backend scripts found`.

---

## App Structure Reference

```text
my-app/
├── automaton.json          ← app link + frontend/backend deploy config (commit this)
├── frontend/
│   ├── src/                ← frontend source
│   ├── public/             ← static frontend assets
│   ├── dist/               ← build output uploaded as public runtime
│   ├── package.json
│   └── vite.config.ts
└── backend/
    ├── *.py                ← Python controllers (filename = controller name)
    ├── requirements.txt    ← pip dependencies (optional)
    ├── shared/             ← shared Python helpers
    └── mocks/
        └── {ctrl}.{action}.json   ← local dev mock responses
```

Notes:
- Default AI-generated local apps should use `frontend/` with a build step and deploy only `frontend/dist/`.
- Plain static root apps are still supported, but only as an explicit no-build choice with `"frontend": { "deployDir": "." }`.
- Source files are local editable project files; public Automaton runtime is produced from `frontend.deployDir`, not from raw frontend source folders.

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

| | Studio App Editor | Local IDE + CLI |
|---|---|---|
| Local setup required | ❌ | ✅ |
| Git workflow | ❌ | ✅ |
| AI knows Automaton structure | ✅ Built-in | ✅ Via local guidance and `llms.txt` |
| Auto-creates `automaton.json` | ✅ | ✅ |
| Multi-app support | ✅ Per app | ✅ Per folder |
| Best for | Quick prototypes | Any IDE, any AI tool |
