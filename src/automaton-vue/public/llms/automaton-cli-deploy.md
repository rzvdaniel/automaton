# Automaton CLI Deploy Workflow

Use this when the user is building an Automaton app locally in Cursor, Copilot, Claude Code, or another IDE and wants to deploy it with the Automaton CLI.

## Goal

Keep the project local, commit it to Git, and deploy the app to Automaton with the CLI.

## Required setup

1. Install the Automaton CLI.
2. Configure the server and API key:

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

The CLI also creates `.automatonignore` in the project root when the project is initialized or linked, if the file does not already exist. It starts with the built-in deploy exclusions like `.automaton/`, `.git/`, `node_modules/`, `bin/`, `obj/`, `automaton.json`, and `.automatonignore` itself.

4. If the app already exists in Automaton Studio, link the local project to it:

```bash
automaton link <app-id>
```

If the app is being created from scratch locally, skip this step. `link` is only for attaching the local folder to an already-existing app.

## What the AI should do

- Build the app in the current local project folder.
- Keep `automaton.json` in the project root.
- Keep `.automatonignore` in the project root and add custom deploy excludes there when needed.
- Follow the Automaton app structure from `llms.txt` and the linked files.
- If the app uses a build step, set `build` and `deployDir` in `automaton.json`.
- Use Python backend controllers in `backend/`.
- Tell the user to deploy with `automaton deploy` after the app is ready.

## Deploy commands

```bash
automaton deploy
automaton deploy --clean
automaton deploy --skip-backend
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
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI to deploy it.
```