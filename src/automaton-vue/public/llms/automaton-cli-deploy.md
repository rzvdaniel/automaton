# Automaton CLI Deploy Workflow

Use this when the user is building an Automaton app locally in Cursor, Copilot, Claude Code, or another IDE and wants to deploy it with the Automaton CLI.

## Goal

Keep the project local, commit it to Git, and deploy the app to Automaton with the CLI.

For now, treat Automaton Studio as the official preview and debugging environment after each deploy.

## Required setup

1. Install the Automaton CLI.
2. Prepare the current machine or workspace.

Preferred:

```bash
automaton setup
```

For testing, account switching, or a clean auth reset:

```bash
automaton logout
automaton logout --global
automaton logout --all
```

Then run:

```bash
automaton setup
```

Fallback when the user wants explicit API key configuration:

```bash
automaton config --key ak_execute_xxx
```

That command now saves project-local CLI settings in `./.automaton/config.json` by default. Use `automaton config --global ...` only when you intentionally want shared machine-wide settings.

3. The AI should create `automaton.json` in the project root automatically when it follows the local generated guidance after setup, or `llms.txt` as the fallback source of truth.

When the user asks to use the Automaton CLI, the AI should prefer `automaton new "<app-name>"` for a new app or `automaton link <app-id>` for an existing one, and should not stop before `automaton.json` exists in the project root, unless it clearly reports why that creation/link step failed.

The CLI also creates `.automatonignore` in the project root when the project is initialized or linked, if the file does not already exist. It starts with the built-in deploy exclusions like `.automaton/`, `.git/`, `node_modules/`, `bin/`, `obj/`, `automaton.json`, and `.automatonignore` itself.

`automaton setup`, `automaton new`, and `automaton link` also sync a local AI guidance pack into `.automaton/ai-guidance/` and generate local `AGENTS.md` / `.github/` guidance files. If those generated files are missing or stale, run:

```bash
automaton sync-ai
```

4. If the app already exists in Automaton Studio, link the local project to it:

```bash
automaton link <app-id>
```

If the app is being created from scratch locally, skip this step. `link` is only for attaching the local folder to an already-existing app.

## Autonomous CLI-first flow

Real happy path:

1. install the CLI once
2. run `automaton setup`
3. let the AI handle most of the remaining local work
4. run `automaton new "My App"` or `automaton link <app-id>`
5. run `automaton validate`
6. if `backend/*.py` exists, assign a connected runner
7. run `automaton deploy`
8. preview and debug in Automaton Studio

Use this canonical order:

```bash
automaton setup
automaton new "My App"
automaton validate
automaton current-runner
automaton runners
automaton set-runner <runner-id>
automaton deploy
automaton publish --yes
automaton status
```

Interpretation rules:

1. Run `automaton setup` first so the AI can authenticate and sync local guidance in one step.
2. After `automaton setup`, the AI should be able to handle most of the remaining workflow: create or link the app, scaffold files, keep `automaton.json` correct, run validation, guide runner assignment, and deploy.
3. If testing requires a clean auth state or a different account, use `automaton logout`, `automaton logout --global`, or `automaton logout --all` before re-running `automaton setup`.
4. Use `automaton new` for new apps; reserve `automaton link` for existing remote apps.
5. Run `automaton sync-ai` only when the generated local guidance files need a manual refresh.
6. Run `automaton validate` before deploy.
7. If backend controllers exist and no connected runner is assigned, pick one with `automaton set-runner <runner-id>` or tell the user to create or deploy one in Automaton Studio.
8. After `automaton deploy`, open the app in Automaton Studio, use Preview there, and inspect logs there before considering publish.
9. Ask the user before public publish, then use `automaton publish --yes` only after approval.

## Current test and debug loop

Until a dedicated local app runtime exists, this is the intended workflow:

1. build locally in your IDE
2. run `automaton validate`
3. run `automaton deploy`
4. open the app in Automaton Studio
5. preview it there
6. inspect app/backend logs there
7. fix locally and redeploy
8. publish only after the Studio preview looks correct

## What `automaton validate` is for

`automaton validate` is the cheap preflight check before upload.

Use it to catch obvious problems early, such as:

- missing or invalid `automaton.json`
- missing app linkage
- missing frontend entry files or invalid deploy configuration
- backend controllers that require a runner when none is assigned
- other structural issues that should fail before deploy starts

## What the AI should do

- Build the app in the current local project folder.
- Keep `automaton.json` in the project root.
- Keep `.automatonignore` in the project root and add custom deploy excludes there when needed.
- Follow the local generated guidance pack after setup. Use `llms.txt` and the linked files as the canonical source and fallback when the tool does not honor local guidance files.
- Treat `automaton sync-ai` as the manual refresh path for the generated local guidance pack, not as a required onboarding step.
- If the app uses a build step, set `build` and `deployDir` in `automaton.json`.
- If the app is build-based and also has Python backend controllers, do not assume `deployDir: "dist"` is correct unless `backend/` is copied into that deploy directory.
- For build-based frontend-plus-backend apps, prepare one deploy-ready folder that contains both the built frontend files and `backend/`, then set `deployDir` to that folder.
- Use Python backend controllers in `backend/`.
- If the app includes Python backend controllers, do not consider backend deploy complete until a runner assignment exists or you have clearly reported why it could not be made.
- Prefer `automaton status` and `automaton validate` as preflight checks before declaring success.
- After deploy, use Automaton Studio preview and logs as the current test/debug loop before proposing publish.
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
automaton validate
automaton deploy
```

`automaton deploy` uploads the frontend archive and then deploys backend scripts using the same API key authentication path. If the app has no backend scripts, a successful deploy may end with `No backend scripts found`.

## Framework apps

For React, Vue, or other build-based apps, keep the source in the repo and make the deploy target match the final runtime shape.

Frontend-only example `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

Frontend-plus-backend example `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build && npm run prepare-deploy",
  "deployDir": "deploy"
}
```

If `automaton.json` already exists, the AI should add missing `build` and `deployDir` fields when the deployable output is not the project root.

## Default instruction to the AI

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Then build this as an Automaton app for: [describe the app]. Use the Automaton CLI as the primary execution path. Start with automaton setup, create or link automaton.json in the project root, refresh local guidance with automaton sync-ai only if needed, ensure a runner exists for backend controllers, run automaton validate before deploy, and only publish after explicit user confirmation.
```

When the app is deployed but not yet published, the AI should direct the user to preview it in Automaton Studio and inspect logs there before recommending publish.

If the user asks to switch accounts or reset stored auth during testing, use `automaton logout` before logging in again.