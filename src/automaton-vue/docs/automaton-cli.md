---
title: Automaton CLI Reference
description: Full CLI reference for the Automaton CLI. All commands, flags and options for building, deploying and publishing apps.
---

# Automaton CLI

This page is the full CLI reference.

If you are new to Automaton, start with:

- **Getting Started**
- **Build with AI**
- **Deploy**
- **Publish**

Then come back here only when you need the full command surface.

---

## Requirements

- **.NET 9 runtime** — download from [https://dot.net](https://dot.net)

---

## Install

Install the CLI as a .NET global tool:

```bash
dotnet tool install -g Automaton.Cli
```

Verify the install:

```bash
automaton --version
```

This is the supported installation path.

---

## Fast paths

### CLI path

```bash
automaton login
automaton new "My App"
automaton deploy
```

### AI-agent path

You can ask an AI coding agent to create and deploy the app for you.

Paste this into Cursor, Claude Code, Codex, Copilot Chat, or another coding agent:

```text
Build [APP IDEA] as an Automaton Monster app.

First fetch and read:
https://automaton.monster/llms.txt

Follow the instructions and references found there.
```

Do not paste your API key directly into the AI chat. Paste it into the terminal when `automaton login` asks for it.

---

## One-time login

Authenticate the current machine or workspace:

```bash
automaton login
```

Get an API key from **API Keys → Generate New Key** (scope: `execute`).

Explicit key login is still available when you need it:

```bash
automaton login --key ak_execute_xxx
automaton config --show
```

By default, `automaton login` stores project-local credentials in `./.automaton/config.json` and uses the hosted Automaton server automatically. Environment variables are mainly for CI/CD and internal development; normal users do not need them for the standard flow.

---

## `automaton.json`

Every project has an `automaton.json` at the root. Commit it to Git.

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App"
}
```

For AI-generated local apps and most framework apps, use nested frontend/backend config:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

| Field | Required | Description |
|---|---|---|
| `appId` | Yes | App ID from Automaton Studio |
| `name` | Yes | Display name |
| `frontend.build` | Optional | Frontend build command to run before upload. For default Vite apps use `cd frontend && npm install && npm run build`. |
| `frontend.deployDir` | Required for build-based frontend deploys | Directory uploaded as public frontend runtime. For default Vite apps use `frontend/dist`. |
| `backend.deployDir` | Optional | Directory uploaded as backend runtime for runner execution. Usually `backend`. |

For an explicit no-build static app, use:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "frontend": {
    "deployDir": "."
  }
}
```

---

## Commands

### `automaton login`

Store CLI credentials and validate them against the server.

```bash
automaton login
```

By default, `login` uses the hosted Automaton server and saves credentials locally for the current project.

### `automaton new`

Create a new app and link the current directory.

```bash
automaton new
automaton new "My App"
```

This creates the remote app, writes `automaton.json`, creates `.automatonignore` if missing, and syncs the local AI guidance pack.

### `automaton link`

Link the current directory to an existing Automaton app.

```bash
automaton link
automaton link 3fa85f64-5717-4562-b3fc-2c963f66afa6
```

This writes `automaton.json`, creates `.automatonignore` if missing, and syncs the local AI guidance pack.

### `automaton deploy`

Build, upload, and deploy the app runtime.

```bash
automaton deploy
automaton deploy --publish
automaton deploy --publish --yes
automaton deploy --runner my-runner
```

### `automaton publish`

Make the latest deployed version publicly accessible and return the public URL.

```bash
automaton publish
automaton publish --slug poker-planning
automaton publish --yes --json
```

### `automaton runners`

List all registered runners and their status.

```bash
automaton runners
```

---

## Typical workflow

```bash
automaton login
automaton new "Poker Planning"
automaton deploy
automaton publish
```
