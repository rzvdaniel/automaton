# Automaton MCP — Instructions

You are connected to Automaton via MCP. You can **discover** apps, **read** files, and **create** new apps.

> **Important:** Writing files and deploying are handled by the **automaton CLI**, not via MCP.
> After generating code to the local filesystem, the developer runs `automaton deploy` to push it to the server.
> MCP is mostly for guidance and discovery. Prefer the CLI for creating or linking the local project that will be deployed.

## Source of truth

For Automaton app structure, frontend conventions, backend controller shape, local mocks, and deploy layout, use the shared app instructions in `llms.txt` and its linked files as the source of truth.

If this MCP prompt conflicts with `llms.txt` or the linked `llms/` files about app layout, backend shape, frontend organization, or deploy shape, follow `llms.txt`.

Read these shared files for app-building guidance:

- `https://automaton.monster/llms.txt`
- `/llms/automaton-cli-deploy.md`
- `/llms/automaton-mcp.md`
- `/llms/automaton-apps-overview.md`
- `/llms/automaton-frontend-structure.md`
- `/llms/automaton-backend-controllers.md`
- `/llms/automaton-starter-prompts.md`

## MCP scope

MCP-specific role:

- use MCP to discover apps and inspect remote files when needed
- create apps through MCP only when the user explicitly asked for MCP app creation or cannot use the CLI creation/link flow
- write frontend and backend files locally with normal file tools
- rely on the shared `llms.txt` guidance for UI conventions, backend contracts, mocks, and deploy shape instead of redefining them here
- use the Automaton CLI flow for setup, app linking or creation, validate, deploy, preview iteration, and publish

Why this matters:

- An MCP API key and the CLI's stored API key can belong to different users or servers.
- If MCP creates an app but the CLI later deploys with another account, the CLI may return 404 because that account cannot see the MCP-created app.
- `automaton new` and `automaton link` avoid this mismatch by writing `automaton.json` using the same CLI credentials that will deploy.

## First: Check for automaton.json

Before doing anything, check if `automaton.json` exists in the project root using your local file tools.

**If it exists** — read `appId` from it. Use that appId in all tool calls. Do not create a new app.

**If it does not exist**, choose the right path based on user intent:

- **The user asked for a CLI-first or normal local app workflow** — run or instruct the developer to run `automaton setup`, then `automaton new "<app-name>"` for a new app or `automaton link <app-id>` for an existing app. Do **not** call `create_app` by default.

- **User provided an app ID** (e.g. "work on app `abc123`", "the app id is ...") — prefer `automaton link <app-id>` so the CLI writes `automaton.json` with the resolved server. Do **not** call `create_app`.

- **User said they want to resume / continue work on an existing app but gave no ID** — first call `list_user_apps` to discover the user's accessible apps. If that still does not identify the right app, ask: *"What is the app ID? You can find it in `automaton.json` on another machine or in the Automaton dashboard."* Once they provide it, prefer `automaton link <app-id>` and proceed as above.

- **User explicitly wants MCP to create the app** — call `create_app` with a short descriptive name. The tool returns the appId. Immediately **overwrite** `automaton.json` in the project root with **only** this content (replace any existing content entirely — never append):

```json
{
  "appId": "<returned-appId>",
  "name": "<app-name>",
  "server": "<Automaton server URL>"
}
```

The file must contain exactly one JSON object. Before deploying, verify that `automaton whoami` is using the same server/account as the MCP key. If the CLI cannot resolve the MCP-created app, relink with `automaton new` or `automaton link` under the CLI account instead of repeatedly restructuring the app files.

`automaton.json` is the project's link to its Automaton app. Commit it to Git so collaborators don't need to reconfigure anything.

## Workflow

1. Check for `automaton.json`.
2. Create or link the app with the CLI (`automaton new` or `automaton link`) unless the user explicitly requested MCP creation.
3. Build app files locally using the shared app rules from `llms.txt`.
4. Use MCP to inspect existing remote files only when needed.
5. Tell the developer to run the Automaton CLI flow (`automaton validate`, `automaton deploy`, and publish later if desired).

**You write files locally. The developer runs the CLI to push them.**

## Resuming Work on an Existing App

When `automaton.json` already exists (pointing to an existing app) but local files are missing or incomplete — for example, on a new machine or in a fresh clone — pull the files down from Automaton first before editing:

For the CLI workflow and commands, use `llms.txt` and `/llms/automaton-cli-deploy.md`.

**Via MCP (if you only need to inspect a few files):**
1. Read `appId` from `automaton.json`
2. Call `get_file_tree(appId)` when you need the folder structure, or `list_files(appId)` when a flat file list is enough
3. Call `read_file(appId, path)` for each file you need
4. Write each file to disk using your local file tools, preserving relative paths
5. Edit locally as needed, then run `automaton deploy`

**Via MCP (if you need to discover which app to inspect first):**
1. Call `list_user_apps`
2. Choose the target appId from the returned list
3. Call `get_file_tree(appId)` to inspect the app's editable source file tree
4. Call `read_file(appId, path)` for the specific files you need

## Available MCP Tools

| Tool | Purpose |
|------|------|
| `create_app` | Create a new app; returns appId to store in `automaton.json` |
| `list_user_apps` | List apps accessible to the current user so you can find an appId |
| `read_file` | Read a file from the server (optionally a line range) |
| `list_files` | List the app's editable source files and structure |
| `get_file_tree` | Return the app's editable source file tree as hierarchical JSON |
| `list_runners` | List available runners (for reference only) |

All write, deploy, and publish operations are handled by the **automaton CLI** (`automaton setup`, `automaton new`, `automaton link`, `automaton validate`, `automaton deploy`, `automaton publish`, etc.). Use `create_app` sparingly because it may not share credentials with the CLI.
