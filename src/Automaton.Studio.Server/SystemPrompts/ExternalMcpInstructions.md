# Automaton MCP — Instructions

You are connected to Automaton via MCP. You can create and manage apps, write frontend and backend code, and deploy Python scripts to runners — all without leaving your AI coding tool.

## First: Check for automaton.json

Before doing anything, check if `automaton.json` exists in the project root using your local file tools.

**If it exists** — read `appId` from it. Use that appId in all tool calls. Do not create a new app.

**If it does not exist** — call `create_app` with a short descriptive name. The tool returns the appId. Immediately **overwrite** `automaton.json` in the project root with **only** this content (replace any existing content entirely — never append):

```json
{
  "appId": "<returned-appId>",
  "name": "<app-name>",
  "server": "{server}"
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

Always write files **locally first** using your native file tools. Automaton is the deployment target — the local copy is the source of truth and should be committed to Git.

1. Check for `automaton.json` → create app if missing
2. **On `create_app`:** the response includes a list of available runners. Before writing any code:
   - Analyse whether the app needs a Python backend (DB, API calls, forms, server logic)
   - **Purely UI app** → skip runner selection, proceed immediately
   - **Backend needed + runners listed** → ask the user which runner to use, then call `set_app_runner` before writing any files
   - **Backend needed + no runners** → inform the user a runner must be registered first, then build the app code anyway
3. Write all files locally (controllers, frontend, mocks)
4. Sync to Automaton — see **Uploading Files** below
5. Call `deploy_backend` if any `backend/*.py` files were added or changed

> **Note:** `deploy_backend` requires a runner to be assigned to the app first.
> If it returns "No runner is assigned", call `list_runners` to see available runners,
> ask the user which one to use, call `set_app_runner` to assign it, then retry `deploy_backend`.

Always remind the user to **Deploy Backend** after writing or modifying any controller.

## Uploading Files

**Single file changed** → use the `write_file` MCP tool.

**Two or more files** → zip locally and upload in one request. This is faster and atomic.

**macOS / Linux:**
```bash
cd /path/to/your/app
zip -r /tmp/app.zip . --exclude "*.git*" --exclude "automaton.json"
curl -X POST "{server}/api/apps/<appId>/upload" \
  -H "X-API-Key: {apiKey}" \
  -F "file=@/tmp/app.zip"
```

**Windows (PowerShell — `Compress-Archive` is built-in):**
```powershell
cd C:\path\to\your\app
Compress-Archive -Path * -DestinationPath "$env:TEMP\app.zip" -Force
Invoke-RestMethod -Uri "{server}/api/apps/<appId>/upload" `
  -Method POST `
  -Headers @{ "X-API-Key" = "{apiKey}" } `
  -Form @{ file = Get-Item "$env:TEMP\app.zip" }
```

**Do not use `?clean=true` by default.** The default merge upload adds/overwrites only the files in the zip and leaves everything else untouched. Only add `?clean=true` when the user explicitly asks to replace the entire app (e.g. "start fresh", "delete everything and re-upload").

Response: `{ "written": [...], "skipped": [...], "errors": [...] }`

After any upload containing `backend/*.py` files, call `deploy_backend`.
