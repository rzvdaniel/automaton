# Automaton MCP — Instructions

You are connected to Automaton via MCP. You can **discover** apps, **read** files, and **create** new apps.

> **Important:** Writing files and deploying are handled by the **automaton CLI**, not via MCP.
> After generating code to the local filesystem, the developer runs `automaton deploy` to push it to the server.
> MCP is for discovery and app creation only.

## Source of truth

For Automaton app structure, frontend conventions, backend controller shape, local mocks, and deploy layout, use the shared app instructions in `llms.txt` and its linked files as the source of truth.

If this MCP prompt conflicts with `llms.txt` or the linked `llms/` files about app layout, backend shape, frontend organization, or deploy shape, follow `llms.txt`.

Read these shared files for app-building guidance:

- `https://automaton.monster/llms.txt`
- `/llms/automaton-cli-deploy.md`
- `/llms/automaton-apps-overview.md`
- `/llms/automaton-frontend-structure.md`
- `/llms/automaton-backend-controllers.md`
- `/llms/automaton-starter-prompts.md`

## MCP scope

MCP-specific role:

- use MCP to discover apps, create apps, and inspect remote files when needed
- write frontend and backend files locally with normal file tools
- rely on the shared `llms.txt` guidance for UI conventions, backend contracts, mocks, and deploy shape instead of redefining them here
- use the Automaton CLI flow for validate, deploy, preview iteration, and publish

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

## Workflow

1. Check for `automaton.json`.
2. Create or link the app via MCP only when that project link is missing.
3. Build app files locally using the shared app rules from `llms.txt`.
4. Use MCP to inspect existing remote files only when needed.
5. Tell the developer to run the Automaton CLI flow (`automaton validate`, `automaton deploy`, and publish later if desired).

**You write files locally. The developer runs the CLI to push them.**

## Resuming Work on an Existing App

When `automaton.json` already exists (pointing to an existing app) but local files are missing or incomplete — for example, on a new machine or in a fresh clone — pull the files down from Automaton first before editing:

For the CLI workflow and commands, use `llms.txt` and `/llms/automaton-cli-deploy.md`.

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
