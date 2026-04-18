# Automaton MCP — Instructions

You are connected to Automaton via MCP. You can **discover** apps, **read** files, and **create** new apps.

> **Important:** Writing files and deploying are handled by the **automaton CLI**, not via MCP.
> After generating code to the local filesystem, the developer runs `automaton deploy` to push it to the server.
> MCP is for discovery and app creation only.

## First: Check for automaton.json

Before doing anything, check if `automaton.json` exists in the project root using your local file tools.

**If it exists** — read `appId` from it. Use that appId in all tool calls. Do not create a new app.

**If it does not exist**, choose the right path based on user intent:

- **User provided an app ID** (e.g. "work on app `abc123`", "the app id is ...") — write `automaton.json` immediately with that ID, then follow the **Resuming Work on an Existing App** section to pull files down before editing. Do **not** call `create_app`.

- **User said they want to resume / continue work on an existing app but gave no ID** — ask: *"What is the app ID? You can find it in `automaton.json` on another machine or in the Automaton dashboard."* Once they provide it, write `automaton.json` and proceed as above.

- **User wants to build a new app** — call `create_app` with a short descriptive name. The tool returns the appId. Immediately **overwrite** `automaton.json` in the project root with **only** this content (replace any existing content entirely — never append):

```json
{
  "appId": "<returned-appId>",
  "name": "<app-name>"
}
```

The file must contain exactly one JSON object.

`automaton.json` is the project's link to its Automaton app. Commit it to Git so collaborators don't need to reconfigure anything.

## Tech Stack

- **Frontend**: HTML, CSS, JavaScript — Tailwind CSS via CDN (no build step required)
  - Single page or multi-page (`index.html`, `about.html`, etc.)
  - Include Tailwind: `<script src="https://cdn.tailwindcss.com"></script>`
- **Backend**: Python scripts in `backend/` — executed by Automaton's runner
  - See backend instructions below for the required controller pattern

## Calling a Controller from the Frontend

```javascript
// callController returns the data object directly (res.data from the backend response).
// It throws on error — always wrap in try/catch.
try {
    const result = await window.automatonApi.callController('todos', 'list', { page: 1 });
    // result IS the data: { items: [...], total: 42 }
    console.log(result.items);   // NOT result.data.items — result already IS the data
} catch (err) {
    console.error(err.message); // show this to the user
}
```

**Critical:** `callController` returns `res.data` from the backend, not the `{success, data, error}` envelope.
Do NOT access `result.success` or `result.data` — those fields do not exist on the returned value.
Errors surface as thrown exceptions, not as `result.success === false`.

`window.automatonApi` is injected by Automaton — do NOT define or override it yourself.

## Local Testing (without a running server)

Create mock response files so the app works offline:

```
backend/mocks/todos.list.json     →  served when callController('todos', 'list') is called locally
backend/mocks/todos.create.json
```

Mock file format — must have a `data` key (callController returns the `data` field):
```json
{ "success": true, "data": { "items": [{ "id": 1, "title": "Demo todo" }], "total": 1 }, "error": "" }
```

The dev polyfill in `index.html` reads these automatically — no server needed for frontend testing.

## Workflow

1. Check for `automaton.json` → create app via `create_app` if missing (writes `automaton.json` locally)
2. Write all files to the **local filesystem** using your native file tools (the AI's built-in create/edit capabilities)
3. Tell the developer to run `automaton deploy` to upload and deploy the backend

**You write files locally. The developer runs the CLI to push them.**

## Deploying with the automaton CLI

Install (requires .NET 9 runtime from https://dot.net):
```bash
# macOS / Linux
curl -fsSL https://your-server/install.sh | bash

# Windows PowerShell
irm https://your-server/install.ps1 | iex

# or via dotnet tool
dotnet tool install -g Automaton.Cli
```

One-time setup:
```bash
automaton config --server https://your-automaton-server --key ak_execute_xxx
```

Deploy your app:
```bash
automaton deploy             # zip the project, upload, deploy backend
automaton deploy --clean     # full replace (wipes existing files first)
automaton deploy --skip-backend  # upload only, no backend deploy
```

Optional fields in `automaton.json` for advanced deploy:
```json
{
  "appId": "...",
  "name": "My App",
  "build": "npm run build",
  "deployDir": "dist"
}
```
- `build` — shell command to run before uploading (e.g. Vite/webpack build)
- `deployDir` — which folder to upload (`dist/` → `frontend/` → project root if not set)

Other CLI commands:
```bash
automaton pull               # download server files to local directory
automaton publish -m "what changed"  # save a version snapshot
automaton runners            # list available runners
automaton init               # create a new app
automaton init --app <id>    # link to an existing app
```

## Resuming Work on an Existing App

When `automaton.json` already exists (pointing to an existing app) but local files are missing or incomplete — for example, on a new machine or in a fresh clone — pull the files down from Automaton first before editing:

**Via CLI (recommended):**
```bash
automaton pull
```

**Via MCP (if you only need to inspect a few files):**
1. Read `appId` from `automaton.json`
2. Call `list_files(appId)` to see all files in the app
3. Call `read_file(appId, path)` for each file you need
4. Write each file to disk using your local file tools, preserving relative paths
5. Edit locally as needed, then run `automaton deploy`

## Available MCP Tools

| Tool | Purpose |
|------|------|
| `create_app` | Create a new app; returns appId to store in `automaton.json` |
| `read_file` | Read a file from the server (optionally a line range) |
| `list_files` | List all files in the app |
| `list_runners` | List available runners (for reference only) |

All write, deploy, and publish operations are handled by the **automaton CLI** (`automaton deploy`, `automaton publish`, etc.).

## Logging in Controllers

Controller actions can use `print()` to emit log lines. These are captured by the runner and surfaced in the Automaton execution logs — visible to the app owner for debugging and monitoring.

**Rules:**
- Log meaningful events: action start with key inputs, row counts, external API calls, branch decisions, errors
- Do NOT log on every trivial step — one or two lines per action is enough
- Do NOT log sensitive data (passwords, tokens, full PII)

```python
def create(self, inputs: dict) -> dict:
    title = inputs.get('title', '').strip()
    print(f"todos.create: title='{title}'")
    if not title:
        print("todos.create: rejected — empty title")
        return {"success": False, "data": None, "error": "title is required"}
    with get_db() as db:
        cur = db.execute("INSERT INTO todos (title, done, created_at) VALUES (?, 0, ?)", (title, datetime.utcnow().isoformat()))
    print(f"todos.create: inserted id={cur.lastrowid}")
    return {"success": True, "data": {"id": cur.lastrowid, "title": title}, "error": ""}
```
