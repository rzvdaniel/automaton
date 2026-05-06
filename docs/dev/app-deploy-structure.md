# Automaton App Deploy Structure

This document explains what an Automaton app should look like at deploy time, how `automaton deploy` chooses what to upload, and how to avoid the two common mistakes:

- using `deployDir: "dist"` and accidentally leaving out `backend/`
- omitting `deployDir` and uploading too much project content

## Key rule

Automaton cares about the deployed app shape on the server, not the source tree shape in your Git repository.

At runtime, a typical deployed app looks like this:

```text
/
├─ index.html
├─ assets/...
├─ backend/...
└─ any other public files
```

The browser-facing frontend files live at the app root. If the app has Python backend controllers, they should also exist in `backend/` at that same deployed root.

## How `automaton deploy` chooses what to upload

Current CLI behavior is:

1. use `deployDir` from `automaton.json` if it is set
2. otherwise use `dist/` if it exists
3. otherwise use `frontend/` if it exists
4. otherwise upload the project root

When the CLI creates or links a project, it also creates `.automatonignore` in the project root if the file does not already exist.

The CLI excludes a small set of local-only folders when it zips content:

- `.automaton`
- `.git`
- `node_modules`
- `obj`
- `bin`
- `.vs`
- `.idea`
- `automaton.json`

These defaults are also written into `.automatonignore` so the project has a visible, editable deploy ignore file.

You can add your own exclude patterns to `.automatonignore`, and the CLI will apply them during packaging whether `deployDir` is set or not.

Example `.automatonignore`:

```text
# Patterns excluded by automaton deploy
.automaton/
.git/
node_modules/
obj/
bin/
.vs/
.idea/
automaton.json
.automatonignore
*.map
coverage/
```

If you omit `deployDir` and the CLI falls back to the project root, `node_modules` is still excluded and will not be uploaded.

So the worst-case `node_modules` upload does **not** happen with the current CLI. But root deploy can still upload too much, such as:

- source files
- build configs
- package manifests
- README files
- local scripts
- any other folders that are not part of the real deployed app

That is still a bad deploy shape even if `node_modules` is filtered out.

## Safe patterns

## Pattern 1: Frontend-only framework app

Use this when the app has no Python backend.

Local project example:

```text
my-app/
├─ src/...
├─ public/...
├─ dist/...
└─ automaton.json
```

Recommended `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build",
  "deployDir": "dist"
}
```

Deployed result:

```text
/
├─ index.html
└─ assets/...
```

This is correct for apps like Pac-Man that are frontend-only.

## Pattern 2: Plain static app with backend in the project root

Use this when the project is already laid out like the deployed app.

Local project example:

```text
my-app/
├─ index.html
├─ styles.css
├─ js/...
├─ backend/
└─ automaton.json
```

Recommended `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster"
}
```

This works because the project root already matches the intended deployed shape.

## Pattern 3: Framework app with backend

This is the one that causes confusion.

Local project example:

```text
my-app/
├─ src/...
├─ backend/
├─ dist/
└─ automaton.json
```

If you set:

```json
{
  "build": "npm run build",
  "deployDir": "dist"
}
```

then only `dist/` is uploaded. That means the root-level `backend/` folder is **not** deployed.

### Correct ways to handle this

### Option A: package a deploy folder that contains both frontend output and backend

Example local structure:

```text
my-app/
├─ src/...
├─ backend/
├─ dist/
├─ deploy/
│  ├─ index.html
│  ├─ assets/...
│  └─ backend/...
└─ automaton.json
```

Recommended `automaton.json`:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "build": "npm run build && npm run prepare-deploy",
  "deployDir": "deploy"
}
```

This is the safest pattern for framework apps with Python backend controllers.

### Option B: copy backend into the built output

If your workflow prefers deploying `dist/`, make sure the build or packaging step copies backend files into `dist/backend/`.

Expected deploy tree:

```text
dist/
├─ index.html
├─ assets/...
└─ backend/...
```

Then `deployDir: "dist"` is valid.

### Option C: deploy the project root only if the root is intentionally clean

This is acceptable only if the project root is already a deploy-ready tree and does not contain unrelated source/build clutter.

For modern framework projects, this is usually **not** the best option.

## Recommended decision rule

- frontend-only framework app: use `deployDir: "dist"`
- plain static app whose root already matches the runtime layout: deploy the root
- framework app with backend: deploy a prepared folder that contains both built frontend files and `backend/`

## What to avoid

Avoid these assumptions:

- `dist/` always works for every app
- deploying the root is harmless because the CLI excludes `node_modules`
- a source-side `frontend/` folder should also exist in the deployed app

The deployed app should be optimized for runtime shape, not for source-code organization.

## Practical guidance for AI-generated apps

When an AI creates or updates `automaton.json`, it should follow these rules:

- if the app is frontend-only and built to `dist/`, set `build` and `deployDir: "dist"`
- if the app has backend controllers outside `dist/`, do not assume `deployDir: "dist"` is sufficient
- if the app has both a framework frontend and a Python backend, prepare one deploy directory that contains both runtime outputs
- add extra deploy-time excludes to `.automatonignore` instead of trying to encode them into `automaton.json`
- if `automaton.json` already exists, add missing fields instead of replacing valid existing ones

## Summary

- `deployDir: "dist"` is correct for frontend-only apps
- `deployDir: "dist"` is wrong for frontend-plus-backend apps unless `backend/` is inside `dist/`
- omitting `deployDir` does not upload `node_modules`, because the CLI excludes it even when deploy falls back to the project root, but it can still upload too much unrelated content
- the safest deploy target is a folder that matches the final runtime app structure exactly