# Automaton CLI Deploy Workflow

Use this when the user is building an Automaton app locally in Cursor, Copilot, Claude Code, or another IDE and wants to deploy it with the Automaton CLI.

## Goal

Keep the project local, commit it to Git, and deploy the app to Automaton with the CLI.

## Required setup

1. Install the Automaton CLI.
2. Authenticate the CLI.

Preferred:

```bash
automaton login
automaton whoami
```

Fallback when the user wants explicit API key configuration:

```bash
automaton config --server https://api.automaton.monster --key ak_execute_xxx
```

That command now saves project-local CLI settings in `./.automaton/config.json` by default. Use `automaton config --global ...` only when you intentionally want shared machine-wide settings.

The CLI also supports keeping `server` in `automaton.json`. Resolution order is:

1. `AUTOMATON_SERVER`
2. `server` in `automaton.json`
3. `./.automaton/config.json`
4. `~/.automaton/config.json`
5. interactive prompt

3. The AI should create `automaton.json` in the project root automatically when it follows `llms.txt`.

When the user asks to use the Automaton CLI, the AI should prefer `automaton create --name "<app-name>"` for a new app or `automaton link <app-id>` for an existing one, and should not stop before `automaton.json` exists in the project root, unless it clearly reports why that creation/link step failed.

The CLI also creates `.automatonignore` in the project root when the project is initialized or linked, if the file does not already exist. It starts with the built-in deploy exclusions like `.automaton/`, `.git/`, `node_modules/`, `bin/`, `obj/`, `automaton.json`, and `.automatonignore` itself.

4. If the app already exists in Automaton Studio, link the local project to it:

```bash
automaton link <app-id>
```

If the app is being created from scratch locally, skip this step. `link` is only for attaching the local folder to an already-existing app.

## Autonomous CLI-first flow

Use this canonical order:

```bash
automaton whoami
automaton create --name "My App"
automaton validate
automaton current-runner
automaton runners
automaton ensure-local-runner --assign
automaton deploy
automaton publish --yes
automaton status
```

Interpretation rules:

1. Run `automaton whoami` first so the AI knows whether login is needed.
2. Use `automaton create` for new apps; reserve `automaton link` for existing remote apps.
3. Run `automaton validate` before deploy.
4. If backend controllers exist and no runner is assigned, prefer `automaton ensure-local-runner --assign` when Docker is available.
5. Ask the user before public publish, then use `automaton publish --yes` only after approval.

## What the AI should do

- Build the app in the current local project folder.
- Keep `automaton.json` in the project root.
- Keep `.automatonignore` in the project root and add custom deploy excludes there when needed.
- Follow the Automaton app structure from `llms.txt` and the linked files.
- If the app uses a build step, set `build` and `deployDir` in `automaton.json`.
- Use Python backend controllers in `backend/`.
- If the app includes Python backend controllers, do not consider backend deploy complete until a runner assignment exists or you have clearly reported why it could not be made.
- Prefer `automaton status` and `automaton validate` as preflight checks before declaring success.
- Ask before public publish, then use `automaton publish --yes` after approval.

## Deploy commands

```bash
automaton deploy
automaton deploy --json
automaton deploy --clean
automaton deploy --skip-backend
```

For backend apps, assign a runner before the first backend deploy:

```bash
automaton current-runner
automaton runners
automaton set-runner <runner-id>
automaton ensure-local-runner --assign
automaton validate
automaton deploy
```

`automaton deploy` uploads the frontend archive and then deploys backend scripts using the same API key authentication path. If the app has no backend scripts, a successful deploy may end with `No backend scripts found`.

## Framework apps

For React, Vue, or other build-based apps, keep the source in the repo and deploy the built output.

Example `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

If `automaton.json` already exists, the AI should add missing `build` and `deployDir` fields when the deployable frontend is built output.

## Default instruction to the AI

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI as the primary execution path. Authenticate with automaton login if needed, create or link automaton.json in the project root, ensure a runner exists for backend controllers, run automaton validate before deploy, and only publish after explicit user confirmation.
```