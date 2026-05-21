# App Source And Runtime Split

## Goal

Automaton apps have editable project files and deployed runtime artifacts. They also have two execution surfaces:

- Frontend: public static browser files.
- Backend: Python/controller files executed by runners.

The storage model must keep both axes explicit:

```text
AppData/apps/{ownerId}/{appId}/
├─ source/
│  ├─ frontend/
│  └─ backend/
├─ runtime/
│  ├─ frontend/
│  └─ backend/
└─ versions/
```

`source/` is editable and forkable. `runtime/` is deployed output. Only `runtime/frontend/` is publicly served. `runtime/backend/` is runner-only and must never be served as public web content.

There is no legacy fallback. If `runtime/frontend/index.html` does not exist, the app is not publicly serveable.

## Folder Contract

### `source/frontend/`

Editable frontend project files. Studio, MCP app tools, GitHub sync, import/export, version snapshots, and source publishing operate here as part of the broader `source/` tree.

Typical Vite app:

```text
source/frontend/
├─ src/
├─ public/
├─ package.json
├─ package-lock.json
├─ vite.config.ts
└─ index.html
```

Simple static app:

```text
source/frontend/
├─ index.html
├─ styles.css
└─ app.js
```

### `source/backend/`

Editable backend controller files and runner package inputs.

```text
source/backend/
├─ main.py
├─ requirements.txt
├─ shared/
└─ mocks/
```

Mocks are source-only test fixtures for Studio preview. They are not public files.

### `runtime/frontend/`

Public static files served by Automaton API/CDN.

```text
runtime/frontend/
├─ index.html
├─ assets/
├─ manifest.webmanifest
└─ sw.js
```

Public serving must read only from this folder and must apply a safe extension allowlist. Unknown or blocked extensions return `404`.

### `runtime/backend/`

Backend package deployed to runner containers.

```text
runtime/backend/
├─ main.py
├─ requirements.txt
└─ shared/
```

This folder is not public. It is accessible only to API-side backend deployment logic and runner deployment transport.

## Local CLI Project Shape

Required default local app shape for AI-generated CLI apps:

```text
my-app/
├─ frontend/
│  ├─ src/
│  ├─ public/
│  ├─ package.json
│  ├─ package-lock.json
│  ├─ vite.config.ts
│  └─ index.html
├─ backend/
│  ├─ main.py
│  └─ requirements.txt
└─ automaton.json
```

The CLI should treat local project root as the source project root. Runtime upload paths are configured explicitly.

Default frontend-plus-backend config:

```json
{
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

AI-generated local apps should always use a frontend build system by default. Public runtime must come from `frontend/dist/`. Do not deploy raw source files from `frontend/`.

Root static frontend remains supported only as an explicit exception:

```json
{
  "frontend": {
    "deployDir": "."
  }
}
```

Use root deployment only when the user explicitly requested a no-build static app or selected a static template such as `automaton new my-app --template static`. The CLI must still exclude sensitive and source-only paths.

## Deploy Behavior

`deployDir` means "what becomes runtime", scoped by surface.

For frontend deploy:

1. Run `frontend.build` when configured.
2. Resolve `frontend.deployDir`.
3. Apply deploy exclusions.
4. Upload zip entries relative to `frontend.deployDir`.
5. API writes the result to `runtime/frontend/`.
6. Public serving reads only `runtime/frontend/`.

For backend deploy:

1. Resolve `backend.deployDir`.
2. Apply backend deploy exclusions.
3. Upload zip entries relative to `backend.deployDir`.
4. API writes the result to `runtime/backend/`.
5. Runner deployment reads only `runtime/backend/`.

For Studio-created apps, backend deployment may sync `source/backend/` into `runtime/backend/` immediately before runner deployment. This makes `runtime/backend/` the deployed package while preserving `source/backend/` as the editable source.

For AI-generated local apps, treat `frontend.build` as required by default, not optional. The default expectation is Vite build output under `frontend/dist/`.

## Safe Exclusions

CLI deploy exclusions should prevent sensitive or irrelevant files from entering runtime.

Always exclude:

```text
.git/
node_modules/
dist/
.env
.env.*
*.key
*.pem
*.pfx
secrets.json
automaton.local.json
```

For frontend runtime, also exclude source-only folders unless they are the configured `deployDir`:

```text
src/
frontend/src/
backend/
```

For backend runtime, exclude frontend-only and build-cache folders:

```text
frontend/
dist/
.vite/
coverage/
```

## Public Serving Policy

Only `runtime/frontend/` is public. Do not serve:

- `source/`
- `runtime/backend/`
- `versions/`
- app root metadata
- previous flat `runtime/` paths
- local fallback folders such as `dist/`, `frontend/`, or root

Allowed public extensions:

```text
.html .htm .css .js .mjs .json .png .jpg .jpeg .gif .svg .webp
.ico .txt .xml .webmanifest .wasm .woff .woff2 .ttf .eot
.mp3 .mp4 .webm .pdf
```

Blocked or unknown extensions return `404`, not `403`.

Cache policy:

- `index.html`: `Cache-Control: no-cache, must-revalidate`
- fingerprinted assets: long cache plus `immutable`
- non-fingerprinted assets: short cache or revalidate

## Forking

Fork copies `source/` only.

Fork is allowed only when:

- `source/` exists
- app is marked forkable
- owner explicitly opted in

Fork never copies:

- `runtime/`
- secrets
- logs
- runner deployment artifacts
- app root metadata that should not belong to the fork

Forked apps should start as private drafts and require their own deploy before public runtime exists.

Recommended app setting:

```json
{
  "visibility": "public",
  "forkable": true
}
```

Default should be `forkable = false`, except for Studio-created templates and curated examples where source sharing is intentional.

## Studio Behavior

Studio editor operates on `source/`.

Recommended source tree display:

```text
source/
├─ frontend/
└─ backend/
```

Frontend preview should serve files from `source/frontend/` or in-memory preview content. Published public app serving should serve from `runtime/frontend/`.

Backend editor writes to `source/backend/`. Deploy Backend packages to `runtime/backend/` and sends that runtime package to the selected runner.

## API Implementation Steps

1. Add storage helpers for:
   - `source/frontend/`
   - `source/backend/`
   - `runtime/frontend/`
   - `runtime/backend/`
2. Change runtime upload to write to `runtime/frontend/`.
3. Change public app serving to read only from `runtime/frontend/`.
4. Add frontend runtime extension allowlist and return `404` for blocked paths.
5. Change backend deploy to sync/package `source/backend/` into `runtime/backend/`.
6. Change runner deployment to read only from `runtime/backend/`.
7. Keep fork, GitHub sync, archive import/export, version snapshots, and MCP tools on `source/`.
8. Update AGENTS.md, generated CLI agent guidance, and app-generation skill prompts to use the four-folder model.

## CLI Implementation Steps

1. Support nested config:

```json
{
  "frontend": {
    "build": "npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

2. Preserve compatibility with current top-level config only during CLI development if needed, but do not add server-side legacy public-serving fallback.
3. Run `frontend.build` before frontend runtime packaging.
4. Zip `frontend.deployDir` into a frontend runtime archive.
5. Zip `backend.deployDir` into a backend runtime archive when present.
6. Upload source separately only for explicit source publishing or forkable templates.
7. Default generated apps to Vite + vanilla TypeScript or JavaScript in `frontend/`.
8. Require frontend build output in `frontend/dist/` for AI-generated local apps.
9. Allow no-build static root apps only when the user explicitly asks for that mode or chooses a static template.

## AI Guidance Update

Generated AGENTS.md and Automaton skill files should say:

- Source is editable/forkable.
- Runtime is deployed output.
- Frontend source lives in `source/frontend/`.
- Backend source lives in `source/backend/`.
- Public runtime lives in `runtime/frontend/`.
- Runner backend runtime lives in `runtime/backend/`.
- Automaton serves only `runtime/frontend/`.
- Fork copies `source/` only.
- Backend deploy packages source backend into runtime backend.
- Prefer Vite + vanilla TypeScript or JavaScript for AI-generated apps.
- Plain static HTML remains supported for very small demos.
- For AI-generated local apps, always create a frontend build pipeline by default.
- Default frontend source goes in `frontend/src/`.
- Default static public assets go in `frontend/public/`.
- Only `frontend/dist/` is deployed as frontend runtime.
- Do not place deployable app files directly in `frontend/` unless the user explicitly requests a no-build static app.

Recommended local AI-generated structure:

```text
frontend/
├─ src/
├─ public/
├─ package.json
└─ vite.config.ts
backend/
├─ main.py
└─ requirements.txt
automaton.json
```

Recommended default config:

```json
{
  "frontend": {
    "build": "npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

## Validation Checklist

- Runtime upload writes to `runtime/frontend/`.
- Frontend clean deploy clears `runtime/frontend/` but not `source/` or `runtime/backend/`.
- Public serving reads `runtime/frontend/index.html`.
- Public serving returns `404` for files outside `runtime/frontend/`.
- Public serving returns `404` for blocked extensions inside `runtime/frontend/`.
- Backend deploy writes/syncs to `runtime/backend/`.
- Runner deployment reads scripts from `runtime/backend/`.
- Backend runtime is not served by public routes.
- Source upload/source publishing writes to `source/`.
- Fork fails when `source/` is missing.
- Fork is hidden or disabled unless `forkable = true`.
- Fork copies `source/` only and does not copy `runtime/`.
- Studio preview reads editable files from `source/frontend/`.
- No server-side fallback to app root, flat `runtime/`, `dist/`, or local `frontend/` paths.
