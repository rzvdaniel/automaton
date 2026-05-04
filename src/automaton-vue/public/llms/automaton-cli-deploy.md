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

3. The AI should create `automaton.json` in the project root automatically when it follows `llms.txt`.

4. If the app already exists in Automaton Studio, link the local project to it:

```bash
automaton link <app-id>
```

If the app is being created from scratch locally, skip this step. `link` is only for attaching the local folder to an already-existing app.

## What the AI should do

- Build the app in the current local project folder.
- Keep `automaton.json` in the project root.
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

## Framework apps

For React, Vue, or other build-based apps, keep the source in the repo and deploy the built output.

Example `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "build": "npm run build",
  "deployDir": "dist"
}
```

## Default instruction to the AI

```text
Read https://automaton.monster/llms.txt, then build this as an Automaton app for: [describe the app]. Use the Automaton CLI to deploy it.
```