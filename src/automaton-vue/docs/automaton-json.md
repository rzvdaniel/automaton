---
title: automaton.json Reference
description: Canonical reference for Automaton app configuration. Learn the required fields, default frontend/backend layout, and unlinked local scaffold shape.
---

# automaton.json Reference

`automaton.json` is the project-level contract for an Automaton app.

Keep it in the project root and commit it to Git.

Typical location:

```text
my-app/
  automaton.json
  frontend/
  backend/
```

## What it controls

- App identity with `appId`
- Friendly app name with `name`
- Optional project server override with `server`
- Frontend build and deploy output with `frontend`
- Backend editable source and runtime directories with `backend`

## Most common shape

For the default local app layout, use:

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
    "sourceDir": "backend",
    "deployDir": "backend"
  }
}
```

This matches the standard local structure:

```text
my-app/
  automaton.json
  .automatonignore
  frontend/
    package.json
    src/
    dist/
  backend/
    *.py
```

## Unlinked local scaffold

If `automaton new` created local files but remote app creation failed, `automaton.json` can exist without `appId` yet.

That is a valid temporary local state.

```json
{
  "name": "My App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "sourceDir": "backend",
    "deployDir": "backend"
  }
}
```

Recover by running one of:

```bash
automaton create
automaton link <app-id>
```

After that succeeds, write `appId` into `automaton.json`.

## Frontend-only app

If the app has no backend, omit the `backend` section:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My Frontend App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  }
}
```

## No-build static app

Use this only for an explicit plain HTML/CSS/JS app with no frontend build step:

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My Static App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "deployDir": "."
  }
}
```

## Field reference

| Field | Required | Meaning |
|---|---|---|
| `appId` | Required for linked apps | Remote Automaton app ID |
| `name` | Yes | Friendly display name |
| `server` | Recommended | Automaton API base URL |
| `frontend.build` | Optional | Command to run before frontend upload |
| `frontend.deployDir` | Required for frontend deploys | Directory uploaded as public frontend runtime |
| `frontend.include` | Optional | Extra frontend include patterns |
| `frontend.exclude` | Optional | Extra frontend exclude patterns |
| `backend.sourceDir` | Required when `backend` exists | Editable backend source directory |
| `backend.deployDir` | Required when `backend` exists | Directory uploaded as runner backend runtime |
| `backend.include` | Optional | Extra backend include patterns |
| `backend.exclude` | Optional | Extra backend exclude patterns |
| `source.include` | Optional | Extra source upload include patterns |
| `source.exclude` | Optional | Extra source upload exclude patterns |

## Behavior notes

- `automaton deploy` uploads `frontend.deployDir` as public frontend runtime.
- `automaton deploy` uploads `backend.deployDir` as backend runtime when present.
- When `backend.sourceDir` and `backend.deployDir` differ, the deploy directory is omitted from the editable source snapshot.
- Interpreted backends may intentionally use `backend` for both directories.
- `frontend/src/` is not public by itself. Only `frontend.deployDir` becomes public runtime.
- Backend runtime files are not publicly served.
- If backend files exist but `backend.sourceDir` or `backend.deployDir` is missing, `automaton validate` and `automaton deploy` fail clearly.

## Related docs

- [Getting Started](/getting-started)
- [Build a Frontend App](/build-frontend-app)
- [Build a Backend App](/build-backend-app)
- [Automaton CLI Reference](/automaton-cli)
