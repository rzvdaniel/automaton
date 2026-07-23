# Automaton App Deploy Structure

This document explains the current deploy contract. It replaces the old "one folder is both source and live output" model.

## Core model

Automaton separates editable source from deployed live output, and each side has frontend/backend surfaces:

```text
AppData/apps/{appId}/
├─ source/
│  ├─ frontend/
│  └─ backend/
└─ live/
   ├─ frontend/
   └─ backend/
```

Rules:
- `source/frontend/` is editable frontend project source.
- `source/backend/` is editable backend Python source.
- `live/frontend/` is the only public static output.
- Studio/API `live/backend/` is deploy output used to send backend files to a runner. It is not editable source and is never publicly served.
- Fork copies `source/` only, never `live/`.

Runner runtime keeps its own private per-app layout:

```text
RUNNER_SCRIPTS_DIR/{appId}/
├─ backend/
├─ venv/
└─ data/
```

Rules:
- `backend/` contains the deployed controller files from `backend.deployDir`.
- `venv/` contains the app-specific Python virtual environment.
- `data/` is private persistent app storage and is not publicly served by default.
- Persistent app files, including SQLite databases and uploads, must be stored under the `AUTOMATON_APP_DATA_DIR` environment variable.
- Do not store persistent files next to controller files in `backend/`; backend deploys can replace controller files.

Recommended runtime data layout:

```text
AUTOMATON_APP_DATA_DIR/
├─ media/
├─ db/
├─ generated/
├─ cache/
├─ logs/
└─ tmp/
```

Guidance:
- `media/` is for uploaded files and other app-owned media.
- `db/` is for SQLite databases and other durable structured storage.
- `generated/` is for exports, reports, generated documents, and similar artifacts.
- `cache/` is for disposable cached data.
- `logs/` is optional and should be used only when an app needs its own log files.
- `tmp/` is for temporary files that are safe to delete.
- Do not create empty folders unless the app needs them.
- Prefer `db/` over a narrower convention such as `sqlite/`.
- Only `media/` currently has platform-aware URL behavior. A file stored under `AUTOMATON_APP_DATA_DIR/media/uploads/...` may be referenced by the app as `/media/uploads/...`.

## CLI deploy contract

For local CLI apps, `automaton.json` now uses explicit nested config:

```json
{
  "appId": "your-app-id",
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

Deploy behavior:
1. Run `frontend.build` when configured.
2. Upload `frontend.deployDir` into `live/frontend/`.
3. Preserve `backend.sourceDir` as editable source.
4. Upload `backend.deployDir` into `live/backend/` when configured.
5. Trigger backend runner deployment from `live/backend/`.
6. Keep runner-side `RUNNER_SCRIPTS_DIR/{appId}/data/` intact across backend deploys.

Packaging rules:
- CLI deploy uploads archives, not one HTTP request per file.
- When `backend.deployDir` differs from `backend.sourceDir`, source packaging
  excludes the generated runtime directory automatically.
- Source packaging must exclude nested dependency and build folders such as `frontend/node_modules/`, `node_modules/`, `frontend/dist/`, and other nested `dist/` outputs. If those leak into the source archive, deploy size and server-side extraction cost spike for no product value.

There is no automatic fallback to `dist/`, `frontend/`, or project root. `frontend.deployDir` must be explicit unless the caller intentionally overrides it with `automaton deploy --dir ...`.

## Recommended local project layout

Default AI-generated local app:

```text
my-app/
├─ automaton.json
├─ frontend/
│  ├─ src/
│  ├─ public/
│  ├─ dist/
│  ├─ package.json
│  └─ vite.config.ts
└─ backend/
   ├─ *.py
   ├─ requirements.txt
   ├─ shared/
   └─ mocks/
```

Why this is the default:
- Vite gives hashed asset filenames, which reduces stale CSS/JS behind Cloudflare.
- Vite can emit relative app-local URLs with `base: './'`, which is required for slug-hosted published apps.
- Public runtime is a clean build artifact from `frontend/dist/`.
- Backend runtime is packaged separately, so frontend output does not need to contain backend files.
- Backend persistence uses `AUTOMATON_APP_DATA_DIR`, not files written beside controllers.

## Asset versioning rule

For build-based apps:
- `index.html` may keep a stable filename.
- CSS, JS, images, fonts, manifests, and worker files should use hashed filenames when the build system supports it.
- Do not hardcode unversioned production asset paths like `/assets/index.css` or `/assets/app.js`.
- Do not emit root-relative app-local URLs such as `/assets/...`, `/manifest.webmanifest`, `/favicon.ico`, or `/`.
- Published app validation rejects root-relative app-local URLs. For Vite, set `base: './'` in `frontend/vite.config.ts`.
- Do not rely on Automaton Studio mutating app HTML to add `?v=` cache-busting.
- Do not manually append query versions to ordinary bundled JS/CSS assets when the build system already emits hashed filenames.
- Exception: for PWA install assets such as `manifest.webmanifest`, manifest-declared icons, `apple-touch-icon`, favicon, splash images, and service worker-controlled install assets, explicitly version active references when replacing the asset.
- Changing the image file alone is not enough. Update every browser-visible reference that may stay cached: the manifest URL, icon URLs inside the manifest, `apple-touch-icon`, favicon when applicable, and the service worker cache name or precache manifest/entries.
- Automaton is responsible for cache headers, CORS headers, and runtime asset serving.

## Explicit no-build static exception

Plain root HTML/CSS/JS remains supported only as an explicit no-build option:

```json
{
  "appId": "your-app-id",
  "name": "My App",
  "frontend": {
    "deployDir": "."
  }
}
```

Use this only for very small static demos. It is not the default for AI-generated apps.

## What to avoid

Avoid these assumptions:
- top-level `build` and `deployDir` still exist in `automaton.json`
- frontend and backend should be merged into one runtime folder
- raw frontend source files should appear in public runtime
- `frontend/src/`, `package.json`, or `vite.config.*` should be publicly visible after deploy

## Summary

- Public runtime comes only from `frontend.deployDir`.
- Runner backend runtime comes only from `backend.deployDir`.
- Editable backend source comes from `backend.sourceDir`.
- Persistent runner app data lives under `AUTOMATON_APP_DATA_DIR` and survives backend redeploys.
- Default AI-generated local apps should use `frontend/` + Vite and deploy only `frontend/dist/`.
- Root static apps remain supported, but only as an explicit no-build exception.
